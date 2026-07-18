using System.Security.Claims;
using CasePlanner.Web.Server.Security;

namespace CasePlanner.Web.Server.Tests;

public sealed class EntraClaimsTests
{
    [Fact]
    public void UsesTenantAndObjectIdsAsDurableSubject()
    {
        const string tenant = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        const string user = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        var principal = Principal(
            new Claim("tid", tenant), new Claim("oid", user), new Claim("name", "Test User"),
            new Claim("preferred_username", "test@example.invalid"), new Claim("roles", "CasePlanner.User"));

        var identity = EntraClaims.FromPrincipal(principal);

        Assert.Equal($"{tenant}:{user}", identity.ExternalSubject);
        Assert.Equal("Test User", identity.DisplayName);
        Assert.Equal("test@example.invalid", identity.Email);
        Assert.Contains("CasePlanner.User", identity.Roles);
    }

    [Fact]
    public void RequiresExactDelegatedScope()
    {
        var principal = Principal(new Claim("scp", "User.Read CasePlanner.Access"));
        Assert.True(EntraClaims.HasScope(principal, "CasePlanner.Access"));
        Assert.False(EntraClaims.HasScope(principal, "CasePlanner.Admin"));
    }

    [Fact]
    public void RejectsIdentityWithoutImmutableClaims()
    {
        var principal = Principal(new Claim("preferred_username", "changeable@example.invalid"));
        Assert.Throws<InvalidOperationException>(() => EntraClaims.FromPrincipal(principal));
    }

    [Fact]
    public void AdministratorRoleBypassesCaseAssignment()
    {
        var options = new EntraOptions { AdministratorAppRole = "CasePlanner.Admin" };
        var administrator = Principal(new Claim("roles", "CasePlanner.Admin"));
        var user = Principal(new Claim("roles", "CasePlanner.User"));

        Assert.True(CaseAccessEvaluator.CanAccessCase(administrator, options, hasAssignment: false));
        Assert.False(CaseAccessEvaluator.CanAccessCase(user, options, hasAssignment: false));
        Assert.True(CaseAccessEvaluator.CanAccessCase(user, options, hasAssignment: true));
    }

    [Theory]
    [InlineData("Owner", true)]
    [InlineData("Collaborator", true)]
    [InlineData("ReadOnly", true)]
    [InlineData("Administrator", false)]
    [InlineData("", false)]
    public void AssignmentRolesAreClosedSet(string role, bool expected) =>
        Assert.Equal(expected, CaseAccessEvaluator.IsValidAssignmentRole(role));

    [Theory]
    [InlineData("Owner", true, true)]
    [InlineData("Collaborator", true, true)]
    [InlineData("ReadOnly", true, false)]
    [InlineData("", false, false)]
    public void AssignmentRolesSeparateReadFromWrite(string role, bool canRead, bool canWrite)
    {
        Assert.Equal(canRead, CaseAccessEvaluator.CanRead(role));
        Assert.Equal(canWrite, CaseAccessEvaluator.CanWrite(role));
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));
}
