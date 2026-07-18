namespace CasePlanner.Web.Server.Security;

public sealed class AssignmentAwareEndpointMetadata;

public sealed class CaseAccessService(
    IHttpContextAccessor accessor,
    SqlServerCaseAssignmentRepository assignments,
    EntraOptions options)
{
    private const string AssignedIdsCacheKey = "CasePlanner.AssignedCaseIds";
    private static string RoleCacheKey(long caseId) => $"CasePlanner.AssignmentRole.{caseId}";

    public bool IsAdministrator =>
        accessor.HttpContext is { } context && CaseAccessEvaluator.IsAdministrator(context.User, options);

    public bool CanCreateCases => !options.Enabled || IsAdministrator;

    public async Task<bool> CanReadAsync(long caseId, CancellationToken token = default) =>
        IsUnrestricted || CaseAccessEvaluator.CanRead(await GetRoleAsync(caseId, token));

    public async Task<bool> CanWriteAsync(long caseId, CancellationToken token = default) =>
        IsUnrestricted || CaseAccessEvaluator.CanWrite(await GetRoleAsync(caseId, token));

    // Null means unrestricted (local authentication disabled or administrator). An empty set means no assignments.
    public async Task<HashSet<long>?> GetVisibleCaseIdsAsync(CancellationToken token = default)
    {
        if (IsUnrestricted) return null;
        var context = accessor.HttpContext;
        if (context?.Items[EntraUserProvisioningMiddleware.ProfileItemKey] is not AuthenticatedUserProfile profile) return [];
        if (context.Items.TryGetValue(AssignedIdsCacheKey, out var cached) && cached is HashSet<long> ids) return ids;
        ids = await assignments.GetAssignedCaseIdsAsync(profile.Id, token);
        context.Items[AssignedIdsCacheKey] = ids;
        return ids;
    }

    private bool IsUnrestricted => !options.Enabled || IsAdministrator;

    private async Task<string?> GetRoleAsync(long caseId, CancellationToken token)
    {
        var context = accessor.HttpContext;
        if (context?.Items[EntraUserProvisioningMiddleware.ProfileItemKey] is not AuthenticatedUserProfile profile) return null;
        var key = RoleCacheKey(caseId);
        if (context.Items.TryGetValue(key, out var cached)) return cached as string;
        var role = await assignments.GetAssignmentRoleAsync(caseId, profile.Id, token);
        context.Items[key] = role ?? string.Empty;
        return role;
    }
}
