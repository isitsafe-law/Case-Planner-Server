namespace CasePlanner.Web.Server.Security;

public sealed class EntraOptions
{
    public const string SectionName = "Authentication:Entra";
    public bool Enabled { get; set; }
    public string SpaClientId { get; set; } = "";
    public string ApiScope { get; set; } = "";
    public string RequiredAppRole { get; set; } = "";
    public string AdministratorAppRole { get; set; } = "CasePlanner.Admin";
    public bool AdministratorPilotOnly { get; set; } = true;
}

public sealed record EntraPublicConfiguration(bool Enabled, string Authority, string ClientId, string ApiScope);
public sealed record AuthenticatedUserProfile(Guid Id, string TenantId, string ObjectId, string DisplayName, string? Email, IReadOnlyList<string> Roles);
public sealed record AppUserSummary(Guid Id, string DisplayName, string? Email, bool IsActive, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? LastLoginUtc);
public sealed record CaseAssignmentRecord(long CaseId, Guid UserId, string DisplayName, string? Email, string AssignmentRole, DateTime AssignedUtc, Guid? AssignedByUserId, string RowVersion);
public sealed record SaveCaseAssignmentRequest(long CaseId, Guid UserId, string AssignmentRole);
public sealed record SetUserActiveRequest(bool IsActive);
