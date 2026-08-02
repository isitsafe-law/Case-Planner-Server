using System.Security.Claims;
using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Security;
using Microsoft.AspNetCore.Http;

namespace CasePlanner.Web.Server.Tests;

// Manager/Administrator Dashboard Milestone 1 (audit/role foundation) coverage:
//  - IApplicationActorContext.Role resolution, all six cases from the spec's priority order.
//  - SqlServerCaseAssignmentRepository.SetUserManagerTierAsync's input validation (the one part of
//    it that doesn't require a live SQL Server connection - there is no live SQL Server sandbox
//    available in this repo, same caveat already noted throughout the dormant multi-user
//    foundation's migrations, so the DB-touching "valid value accepted / audit row written" behavior
//    isn't exercised here).
//  - CasePlannerRepository.RecordActivityAsync (the real, DB-backed SQLite path) stamping
//    actor_role_at_action on every write while leaving field_changed/previous_value/new_value null
//    unless the optional parameters are supplied - mirrors QuickActionActivityAndDiscoveryTests's
//    existing ActivityAudit_PersistsAuthenticatedActorOnEntryAndEditHistory pattern.
public class ManagerTierAndActorRoleTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // --- IApplicationActorContext.Role resolution ---

    private static HttpApplicationActorContext BuildContext(AuthenticatedUserProfile? profile, bool isAdministratorClaim, EntraOptions? options = null)
    {
        options ??= new EntraOptions();
        var identity = isAdministratorClaim
            ? new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, options.AdministratorAppRole) }, "TestAuth")
            : new ClaimsIdentity();
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        if (profile is not null) httpContext.Items[EntraUserProvisioningMiddleware.ProfileItemKey] = profile;
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        return new HttpApplicationActorContext(accessor, options);
    }

    private static AuthenticatedUserProfile Profile(bool isManager, string? managerTier, bool isLegalAssistant = false) =>
        new(Guid.NewGuid(), "tenant", "object", "Jane Doe", "jane@example.com", new List<string>(), isManager, managerTier, isLegalAssistant);

    [Fact]
    public void Role_AdministratorClaim_ReturnsAdministrator_EvenWithChiefCounselTier()
    {
        // (a) beats every tier/manager check below - an Administrator who also happens to be tiered
        // still resolves to "Administrator".
        var actor = BuildContext(Profile(isManager: true, managerTier: "ChiefCounsel"), isAdministratorClaim: true);
        Assert.Equal("Administrator", actor.Role);
    }

    [Fact]
    public void Role_ChiefCounselTier_ReturnsChiefCounsel()
    {
        var actor = BuildContext(Profile(isManager: true, managerTier: "ChiefCounsel"), isAdministratorClaim: false);
        Assert.Equal("Chief Counsel", actor.Role);
    }

    [Fact]
    public void Role_DeputyChiefCounselTier_ReturnsDeputyChiefCounsel()
    {
        var actor = BuildContext(Profile(isManager: true, managerTier: "DeputyChiefCounsel"), isAdministratorClaim: false);
        Assert.Equal("Deputy Chief Counsel", actor.Role);
    }

    [Fact]
    public void Role_ManagerWithNoTier_ReturnsManager()
    {
        var actor = BuildContext(Profile(isManager: true, managerTier: null), isAdministratorClaim: false);
        Assert.Equal("Manager", actor.Role);
    }

    [Fact]
    public void Role_AuthenticatedNonManagerProfile_ReturnsAttorney()
    {
        var actor = BuildContext(Profile(isManager: false, managerTier: null), isAdministratorClaim: false);
        Assert.Equal("Attorney", actor.Role);
    }

    [Fact]
    public void Role_LegalAssistantProfile_ReturnsLegalAssistant()
    {
        var actor = BuildContext(Profile(isManager: false, managerTier: null, isLegalAssistant: true), isAdministratorClaim: false);
        Assert.Equal("Legal Assistant", actor.Role);
    }

    [Fact]
    public void Role_ManagerStillWinsOverLegalAssistantClaim()
    {
        var actor = BuildContext(Profile(isManager: true, managerTier: null, isLegalAssistant: true), isAdministratorClaim: false);
        Assert.Equal("Manager", actor.Role);
    }

    [Fact]
    public void Role_NoProfileOnHttpContext_ReturnsLocalDevelopmentUser()
    {
        var actor = BuildContext(profile: null, isAdministratorClaim: false);
        Assert.Equal("Local development user", actor.Role);
    }

    [Fact]
    public void Role_NoHttpContextAtAll_ReturnsLocalDevelopmentUser()
    {
        var actor = new HttpApplicationActorContext(new HttpContextAccessor(), new EntraOptions());
        Assert.Equal("Local development user", actor.Role);
    }

    [Fact]
    public void LocalApplicationActorContext_Role_MatchesAuditLabel()
    {
        var actor = new LocalApplicationActorContext();
        Assert.Equal("Local development user", actor.Role);
        Assert.Equal(actor.AuditLabel, actor.Role);
    }

    // --- SetUserManagerTierAsync validation (no DB connection reached) ---

    private sealed class NeverConnectFactory : IDatabaseConnectionFactory
    {
        public string Provider => "SqlServer";
        public System.Data.Common.DbConnection CreateConnection() =>
            throw new InvalidOperationException("SetUserManagerTierAsync should reject an invalid tier before ever opening a connection.");
    }

    [Theory]
    [InlineData("SomethingElse")]
    [InlineData("")]
    [InlineData("chiefcounsel")] // case-sensitive - not one of the two exact allowed values
    public async Task SetUserManagerTierAsync_InvalidValue_ThrowsArgumentException_WithoutTouchingTheDatabase(string invalidTier)
    {
        var repository = new SqlServerCaseAssignmentRepository(new NeverConnectFactory());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.SetUserManagerTierAsync(Guid.NewGuid(), invalidTier, Guid.NewGuid()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("DeputyChiefCounsel")]
    [InlineData("ChiefCounsel")]
    public async Task SetUserManagerTierAsync_ValidValue_PassesValidationAndReachesTheDatabaseLayer(string? validTier)
    {
        // Valid values pass the guard clause and proceed to open a connection (which then fails,
        // since NeverConnectFactory has none) - this is the boundary this repo can exercise without
        // a live SQL Server instance. It proves the three allowed values are accepted by the
        // validation, distinct from the rejected values above.
        var repository = new SqlServerCaseAssignmentRepository(new NeverConnectFactory());
        var ex = await Record.ExceptionAsync(() =>
            repository.SetUserManagerTierAsync(Guid.NewGuid(), validTier, Guid.NewGuid()));
        Assert.IsNotType<ArgumentException>(ex);
    }

    // --- RecordActivityAsync stamping (SQLite, real repository) ---

    [Fact]
    public async Task RecordActivityAsync_StampsActorRoleAtAction_AndLeavesDiffFieldsNullByDefault()
    {
        var userId = Guid.NewGuid();
        await using var fixture = await RepositoryTestFixture.CreateAsync(new RoleTestActor(userId, "Deputy Chief Counsel"));
        var c = await fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName = "Role Audit Fixture", County = "Pulaski", Status = "Active", Track = "Contested" });

        var entry = await fixture.Repository.RecordActivityAsync(c.Id, "Other", "Routine note", null);

        Assert.Equal("Deputy Chief Counsel", entry.ActorRoleAtAction);
        Assert.Null(entry.FieldChanged);
        Assert.Null(entry.PreviousValue);
        Assert.Null(entry.NewValue);

        var saved = Assert.Single(await fixture.Repository.GetActivityLogAsync(c.Id), x => x.Id == entry.Id);
        Assert.Equal("Deputy Chief Counsel", saved.ActorRoleAtAction);
        Assert.Null(saved.FieldChanged);
        Assert.Null(saved.PreviousValue);
        Assert.Null(saved.NewValue);
    }

    [Fact]
    public async Task RecordActivityAsync_WithDiffFieldsSupplied_PersistsAndRoundTripsThem()
    {
        var userId = Guid.NewGuid();
        await using var fixture = await RepositoryTestFixture.CreateAsync(new RoleTestActor(userId, "Chief Counsel"));
        var c = await fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName = "Diff Audit Fixture", County = "Pulaski", Status = "Active", Track = "Contested" });

        var entry = await fixture.Repository.RecordActivityAsync(c.Id, "Other", "Reassigned", null, "CurrentHolder", "Legal Assistant", "Attorney");

        Assert.Equal("Chief Counsel", entry.ActorRoleAtAction);
        Assert.Equal("CurrentHolder", entry.FieldChanged);
        Assert.Equal("Legal Assistant", entry.PreviousValue);
        Assert.Equal("Attorney", entry.NewValue);

        var saved = Assert.Single(await fixture.Repository.GetActivityLogAsync(c.Id), x => x.Id == entry.Id);
        Assert.Equal("CurrentHolder", saved.FieldChanged);
        Assert.Equal("Legal Assistant", saved.PreviousValue);
        Assert.Equal("Attorney", saved.NewValue);
    }

    private sealed record RoleTestActor(Guid Id, string RoleLabel) : IApplicationActorContext
    {
        public Guid? UserId => Id;
        public string AuditLabel => RoleLabel;
        public string Role => RoleLabel;
    }
}
