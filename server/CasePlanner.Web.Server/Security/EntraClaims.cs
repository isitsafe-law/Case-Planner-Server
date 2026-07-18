using System.Security.Claims;

namespace CasePlanner.Web.Server.Security;

public sealed record EntraIdentity(string TenantId, string ObjectId, string ExternalSubject, string DisplayName, string? Email, IReadOnlyList<string> Roles);

public static class EntraClaims
{
    private const string ObjectIdUri = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string TenantIdUri = "http://schemas.microsoft.com/identity/claims/tenantid";

    public static EntraIdentity FromPrincipal(ClaimsPrincipal principal)
    {
        var tenantId = Claim(principal, "tid", TenantIdUri);
        var objectId = Claim(principal, "oid", ObjectIdUri);
        if (!Guid.TryParse(tenantId, out _) || !Guid.TryParse(objectId, out _))
            throw new InvalidOperationException("The Entra token does not contain valid tid and oid user claims.");

        var displayName = principal.FindFirstValue("name")
            ?? principal.Identity?.Name
            ?? "Entra user";
        var email = principal.FindFirstValue("preferred_username") ?? principal.FindFirstValue(ClaimTypes.Email);
        var roles = principal.FindAll("roles").Select(c => c.Value)
            .Concat(principal.FindAll(ClaimTypes.Role).Select(c => c.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        return new(tenantId!, objectId!, $"{tenantId}:{objectId}", displayName, email, roles);
    }

    public static bool HasScope(ClaimsPrincipal principal, string requiredScope)
    {
        if (string.IsNullOrWhiteSpace(requiredScope)) return false;
        return principal.FindAll("scp").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(requiredScope, StringComparer.Ordinal);
    }

    private static string? Claim(ClaimsPrincipal principal, params string[] claimTypes) =>
        claimTypes.Select(principal.FindFirstValue).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
