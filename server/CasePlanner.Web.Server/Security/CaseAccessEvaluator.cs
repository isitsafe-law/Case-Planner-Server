using System.Security.Claims;

namespace CasePlanner.Web.Server.Security;

public static class CaseAccessEvaluator
{
    public static bool IsAdministrator(ClaimsPrincipal principal, EntraOptions options) =>
        !string.IsNullOrWhiteSpace(options.AdministratorAppRole)
        && (principal.IsInRole(options.AdministratorAppRole)
            || principal.FindAll("roles").Any(c => c.Value.Equals(options.AdministratorAppRole, StringComparison.OrdinalIgnoreCase)));

    public static bool CanAccessCase(ClaimsPrincipal principal, EntraOptions options, bool hasAssignment) =>
        IsAdministrator(principal, options) || hasAssignment;

    public static bool IsValidAssignmentRole(string? role) =>
        role is "Owner" or "Collaborator" or "ReadOnly";

    public static bool IsValidCaseRole(string? role) =>
        role is "Attorney" or "LegalAssistant" or "Other";

    public static bool CanRead(string? assignmentRole) => IsValidAssignmentRole(assignmentRole);

    public static bool CanWrite(string? assignmentRole) => assignmentRole is "Owner" or "Collaborator";
}
