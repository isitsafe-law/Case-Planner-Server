using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Security;
using Microsoft.AspNetCore.Http;

namespace CasePlanner.Web.Server.Tests;

// Manager/Administrator Dashboard Milestone 4 coverage: CaseAccessService.IsManagerTierOrHigher/
// IsLegalAssistant and their effect on IsUnrestricted (private, exercised here through
// CanReadAsync/CanWriteAsync/GetVisibleCaseIdsAsync - the public surface every
// AssignmentAwareEndpointMetadata-tagged endpoint actually calls). A Manager/Chief Counsel/Deputy
// Chief Counsel/Legal Assistant gets the same unrestricted access Administrator already has,
// decided explicitly with the user so the Manager Dashboard's division-wide visibility works
// without a separate, narrower "all cases" query path, and so a Legal Assistant's narrower default
// dashboard view (Staff Directory-linked case scope) stays a client-side concern rather than a
// second access-control path. Entra is
// enabled in every fixture here specifically to exercise the restricted-vs-unrestricted branch -
// the existing !options.Enabled-means-unrestricted convention is intentionally bypassed by setting
// Enabled=true, matching how a real deployment would behave.
public class CaseAccessServiceTests
{
    private sealed class NeverConnectFactory : IDatabaseConnectionFactory
    {
        public string Provider => "SqlServer";
        public System.Data.Common.DbConnection CreateConnection() =>
            throw new InvalidOperationException("IsUnrestricted-true paths must short-circuit before ever touching the assignment repository.");
    }

    private static CaseAccessService BuildService(AuthenticatedUserProfile? profile, bool isAdministratorClaim = false)
    {
        var options = new EntraOptions { Enabled = true };
        var identity = isAdministratorClaim
            ? new System.Security.Claims.ClaimsIdentity(new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, options.AdministratorAppRole) }, "TestAuth")
            : new System.Security.Claims.ClaimsIdentity();
        var httpContext = new DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(identity) };
        if (profile is not null) httpContext.Items[EntraUserProvisioningMiddleware.ProfileItemKey] = profile;
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var assignments = new SqlServerCaseAssignmentRepository(new NeverConnectFactory());
        return new CaseAccessService(accessor, assignments, options);
    }

    private static AuthenticatedUserProfile Profile(bool isManager, string? managerTier, bool isLegalAssistant = false) =>
        new(Guid.NewGuid(), "tenant", "object", "Jane Doe", "jane@example.com", new List<string>(), isManager, managerTier, isLegalAssistant);

    [Fact]
    public async Task PlainManager_IsUnrestricted_SeesEveryCase_WithoutTouchingAssignmentRepository()
    {
        var service = BuildService(Profile(isManager: true, managerTier: null));

        Assert.True(await service.CanReadAsync(caseId: 999_999));
        Assert.True(await service.CanWriteAsync(caseId: 999_999));
        Assert.Null(await service.GetVisibleCaseIdsAsync());
    }

    [Fact]
    public async Task ChiefCounselTier_IsUnrestricted_EvenWithoutTheIsManagerFlag()
    {
        // manager_tier is orthogonal to is_manager (see Milestone 1's EntraOptions.cs doc comment) -
        // a Chief Counsel might not separately be flagged IsManager=true.
        var service = BuildService(Profile(isManager: false, managerTier: "ChiefCounsel"));

        Assert.True(await service.CanWriteAsync(caseId: 999_999));
        Assert.Null(await service.GetVisibleCaseIdsAsync());
    }

    [Fact]
    public async Task DeputyChiefCounselTier_IsUnrestricted_EvenWithoutTheIsManagerFlag()
    {
        var service = BuildService(Profile(isManager: false, managerTier: "DeputyChiefCounsel"));

        Assert.True(await service.CanWriteAsync(caseId: 999_999));
        Assert.Null(await service.GetVisibleCaseIdsAsync());
    }

    [Fact]
    public async Task LegalAssistant_IsUnrestricted_SeesEveryCase_WithoutTouchingAssignmentRepository()
    {
        // Decided explicitly with the user: a Legal Assistant gets the same unrestricted access a
        // Manager has, rather than being scoped to their supported attorneys' cases at this layer -
        // that narrower default view is a client-side dashboard concern, not access control.
        var service = BuildService(Profile(isManager: false, managerTier: null, isLegalAssistant: true));

        Assert.True(await service.CanReadAsync(caseId: 999_999));
        Assert.True(await service.CanWriteAsync(caseId: 999_999));
        Assert.Null(await service.GetVisibleCaseIdsAsync());
    }

    [Fact]
    public async Task Administrator_IsUnrestricted_AsBefore()
    {
        var service = BuildService(Profile(isManager: false, managerTier: null), isAdministratorClaim: true);

        Assert.True(await service.CanWriteAsync(caseId: 999_999));
        Assert.Null(await service.GetVisibleCaseIdsAsync());
    }

    [Fact]
    public async Task OrdinaryAuthenticatedProfile_NotManagerOrAdmin_IsRestricted()
    {
        var service = BuildService(Profile(isManager: false, managerTier: null));

        // No assignment repository call ever succeeds against NeverConnectFactory, so a restricted
        // profile's assignment lookup throwing here (rather than returning true unrestricted) is
        // itself proof this profile is NOT unrestricted - CanReadAsync's real assignment lookup is
        // exercised (and would need a live connection) once IsUnrestricted is false.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CanReadAsync(caseId: 999_999));
    }

    [Fact]
    public async Task NoProfileAtAll_IsRestricted_ReturnsEmptyVisibleSet()
    {
        var service = BuildService(profile: null);

        Assert.Empty((await service.GetVisibleCaseIdsAsync())!);
    }
}
