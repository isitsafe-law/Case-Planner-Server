using CasePlanner.Data;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Security;

// Multi-user rollout Phase 4c (notifications: admin system-wide inclusion) added is_administrator
// and EntraOptions below. "Administrator" was previously a pure per-request claims check
// (CaseAccessEvaluator.IsAdministrator) with no durable row - fine for request-scoped access checks,
// but a BackgroundService (DeadlineReminderBackgroundService) has no ClaimsPrincipal to check against
// a timer tick. is_administrator is populated here, at login, from the same Entra app-role claim
// (identity.Roles) CaseAccessEvaluator already checks - "as of last login", the same accepted
// staleness window as last_login_utc itself, not a live Graph lookup.
public sealed class SqlServerAppUserRepository(IDatabaseConnectionFactory connectionFactory, EntraOptions entraOptions)
{
    public async Task<AuthenticatedUserProfile> ProvisionAsync(EntraIdentity identity, CancellationToken cancellationToken = default)
    {
        var isAdministrator = identity.Roles.Contains(entraOptions.AdministratorAppRole, StringComparer.OrdinalIgnoreCase);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var id = await UpdateAsync(connection, identity, isAdministrator, cancellationToken);
        if (id is null)
        {
            try { id = await InsertAsync(connection, identity, isAdministrator, cancellationToken); }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                id = await UpdateAsync(connection, identity, isAdministrator, cancellationToken);
            }
        }
        if (id is null) throw new InvalidOperationException("The authenticated Entra user could not be provisioned.");
        return new(id.Value, identity.TenantId, identity.ObjectId, identity.DisplayName, identity.Email, identity.Roles);
    }

    private static async Task<Guid?> UpdateAsync(System.Data.Common.DbConnection connection, EntraIdentity identity, bool isAdministrator, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.app_users
            SET display_name=@name, email=@email, entra_tenant_id=@tenant, entra_object_id=@object,
                is_administrator=@isAdministrator, last_login_utc=SYSUTCDATETIME(), updated_utc=SYSUTCDATETIME()
            OUTPUT INSERTED.id
            WHERE external_subject=@subject AND is_active=1
            """;
        AddParameters(command, identity, isAdministrator);
        var value = await command.ExecuteScalarAsync(token);
        return value is Guid id ? id : null;
    }

    private static async Task<Guid> InsertAsync(System.Data.Common.DbConnection connection, EntraIdentity identity, bool isAdministrator, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.app_users (external_subject, entra_tenant_id, entra_object_id, display_name, email, is_administrator, last_login_utc)
            OUTPUT INSERTED.id
            VALUES (@subject,@tenant,@object,@name,@email,@isAdministrator,SYSUTCDATETIME())
            """;
        AddParameters(command, identity, isAdministrator);
        return (Guid)(await command.ExecuteScalarAsync(token) ?? throw new InvalidOperationException("User insert returned no identifier."));
    }

    private static void AddParameters(System.Data.Common.DbCommand command, EntraIdentity identity, bool isAdministrator)
    {
        command.Parameters.Add(new SqlParameter("@subject", identity.ExternalSubject));
        command.Parameters.Add(new SqlParameter("@tenant", Guid.Parse(identity.TenantId)));
        command.Parameters.Add(new SqlParameter("@object", Guid.Parse(identity.ObjectId)));
        command.Parameters.Add(new SqlParameter("@name", identity.DisplayName));
        command.Parameters.Add(new SqlParameter("@email", (object?)identity.Email ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@isAdministrator", isAdministrator));
    }

    // Multi-user rollout Phase 4c: resolves the recipients for Part A's admin-union - every currently
    // active administrator, for the TaskCompleted and DeadlineReminder triggers to union into their
    // normal case-specific recipient list at the caller (SqlServerChecklistStore.SaveAsync,
    // DeadlineReminderBackgroundService.ScanAsync). Kept here, next to the rest of the app_users
    // querying, rather than on the shared notification-insert method, which stays unaware of "admin"
    // as a concept.
    public async Task<List<Guid>> GetAllAdministratorUserIdsAsync(CancellationToken token = default)
    {
        var result = new List<Guid>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM dbo.app_users WHERE is_administrator=1 AND is_active=1";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(reader.GetGuid(0));
        return result;
    }
}
