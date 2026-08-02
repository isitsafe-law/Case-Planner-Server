namespace CasePlanner.Web.Server.Security;

public sealed class EntraOptions
{
    public const string SectionName = "Authentication:Entra";
    public bool Enabled { get; set; }
    public string SpaClientId { get; set; } = "";
    public string ApiScope { get; set; } = "";
    public string RequiredAppRole { get; set; } = "";
    public string AdministratorAppRole { get; set; } = "CasePlanner.Admin";
    // Optional role claim used to route non-manager users to the Legal Assistant dashboard.
    // Keeping this configurable lets the Entra app registration use its existing naming policy.
    public string LegalAssistantAppRole { get; set; } = "CasePlanner.LegalAssistant";
    public bool AdministratorPilotOnly { get; set; } = true;
}

public sealed record EntraPublicConfiguration(bool Enabled, string Authority, string ClientId, string ApiScope);
// ManagerTier is null ("no tier"), "DeputyChiefCounsel", or "ChiefCounsel" - see 056_manager_tier.sql.
// It is separate from IsManager (039_manager_flag.sql): a user can be IsManager with no tier (an
// ordinary Manager), or hold a tier. Validation of the allowed string values lives in
// SqlServerCaseAssignmentRepository.SetUserManagerTierAsync, not a DB constraint.
public sealed record AuthenticatedUserProfile(Guid Id, string TenantId, string ObjectId, string DisplayName, string? Email, IReadOnlyList<string> Roles, bool IsManager, string? ManagerTier, bool IsLegalAssistant = false);
public sealed record AppUserSummary(Guid Id, string DisplayName, string? Email, bool IsActive, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? LastLoginUtc, bool IsManager, string? ManagerTier);
public sealed record CaseAssignmentRecord(long CaseId, Guid UserId, string DisplayName, string? Email, string AssignmentRole, string CaseRole, DateTime AssignedUtc, Guid? AssignedByUserId, string RowVersion);
public sealed record SaveCaseAssignmentRequest(long CaseId, Guid UserId, string AssignmentRole, string CaseRole);
public sealed record SetUserActiveRequest(bool IsActive);
public sealed record SetUserManagerRequest(bool IsManager);
public sealed record SetUserManagerTierRequest(string? ManagerTier);
