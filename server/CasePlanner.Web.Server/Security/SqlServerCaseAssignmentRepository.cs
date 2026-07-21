using CasePlanner.Data;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace CasePlanner.Web.Server.Security;

public sealed class SqlServerCaseAssignmentRepository(IDatabaseConnectionFactory connectionFactory)
{
    public async Task<List<AppUserSummary>> GetUsersAsync(CancellationToken token = default)
    {
        var result = new List<AppUserSummary>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,display_name,email,is_active,created_utc,updated_utc,last_login_utc FROM dbo.app_users ORDER BY display_name,email";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetBoolean(3), reader.GetDateTime(4), reader.GetDateTime(5), reader.IsDBNull(6) ? null : reader.GetDateTime(6)));
        return result;
    }

    public async Task<List<CaseAssignmentRecord>> GetAssignmentsAsync(long? caseId = null, Guid? userId = null, CancellationToken token = default)
    {
        var result = new List<CaseAssignmentRecord>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ca.case_id,ca.user_id,u.display_name,u.email,ca.assignment_role,ca.case_role,ca.assigned_utc,ca.assigned_by_user_id,ca.row_version
            FROM dbo.case_assignments ca JOIN dbo.app_users u ON u.id=ca.user_id
            WHERE (@caseId IS NULL OR ca.case_id=@caseId) AND (@userId IS NULL OR ca.user_id=@userId)
            ORDER BY ca.case_id,u.display_name
            """;
        command.Parameters.Add(new SqlParameter("@caseId", (object?)caseId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@userId", (object?)userId ?? DBNull.Value));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new(reader.GetInt64(0), reader.GetGuid(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetDateTime(6), reader.IsDBNull(7) ? null : reader.GetGuid(7), Convert.ToBase64String((byte[])reader.GetValue(8))));
        return result;
    }

    public async Task<bool> HasAssignmentAsync(long caseId, Guid userId, CancellationToken token = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP (1) 1 FROM dbo.case_assignments ca JOIN dbo.app_users u ON u.id=ca.user_id WHERE ca.case_id=@caseId AND ca.user_id=@userId AND u.is_active=1";
        command.Parameters.Add(new SqlParameter("@caseId", caseId));
        command.Parameters.Add(new SqlParameter("@userId", userId));
        return await command.ExecuteScalarAsync(token) is not null;
    }

    public async Task<string?> GetAssignmentRoleAsync(long caseId, Guid userId, CancellationToken token = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP (1) ca.assignment_role FROM dbo.case_assignments ca JOIN dbo.app_users u ON u.id=ca.user_id WHERE ca.case_id=@caseId AND ca.user_id=@userId AND u.is_active=1";
        command.Parameters.Add(new SqlParameter("@caseId", caseId));
        command.Parameters.Add(new SqlParameter("@userId", userId));
        return Convert.ToString(await command.ExecuteScalarAsync(token));
    }

    // Multi-user rollout Phase 4a (notifications core): resolves the recipients for the
    // "task completed" trigger - the case's active assignees carrying a given case_role (Attorney,
    // in practice). Kept separate from the shared notification-insert method so that method stays
    // generic (just recipient ids in, notifications out) rather than coupled to case_assignments.
    public async Task<List<Guid>> GetCaseRoleUserIdsAsync(long caseId, string caseRole, CancellationToken token = default)
    {
        var result = new List<Guid>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ca.user_id FROM dbo.case_assignments ca JOIN dbo.app_users u ON u.id=ca.user_id WHERE ca.case_id=@caseId AND ca.case_role=@caseRole AND u.is_active=1";
        command.Parameters.Add(new SqlParameter("@caseId", caseId));
        command.Parameters.Add(new SqlParameter("@caseRole", caseRole));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(reader.GetGuid(0));
        return result;
    }

    // Multi-user rollout Phase 4b (deadline reminders): resolves EVERY active assignee on a case,
    // regardless of case_role - unlike GetCaseRoleUserIdsAsync above (Attorney-only, used by the
    // task-completed trigger), a deadline reminder is relevant to all assigned staff.
    public async Task<List<Guid>> GetAllAssignedUserIdsAsync(long caseId, CancellationToken token = default)
    {
        var result = new List<Guid>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ca.user_id FROM dbo.case_assignments ca JOIN dbo.app_users u ON u.id=ca.user_id WHERE ca.case_id=@caseId AND u.is_active=1";
        command.Parameters.Add(new SqlParameter("@caseId", caseId));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(reader.GetGuid(0));
        return result;
    }

    public async Task<HashSet<long>> GetAssignedCaseIdsAsync(Guid userId, CancellationToken token = default)
    {
        var result = new HashSet<long>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ca.case_id FROM dbo.case_assignments ca JOIN dbo.app_users u ON u.id=ca.user_id WHERE ca.user_id=@userId AND u.is_active=1";
        command.Parameters.Add(new SqlParameter("@userId", userId));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(reader.GetInt64(0));
        return result;
    }

    public async Task<CaseAssignmentRecord> SaveAssignmentAsync(SaveCaseAssignmentRequest request, Guid actorUserId, CancellationToken token = default)
    {
        if (!CaseAccessEvaluator.IsValidAssignmentRole(request.AssignmentRole))
            throw new ArgumentException("AssignmentRole must be Owner, Collaborator, or ReadOnly.");
        if (!CaseAccessEvaluator.IsValidCaseRole(request.CaseRole))
            throw new ArgumentException("CaseRole must be Attorney, LegalAssistant, or Other.");

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE dbo.case_assignments SET assignment_role=@role,case_role=@caseRole,assigned_utc=SYSUTCDATETIME(),assigned_by_user_id=@actor
            WHERE case_id=@caseId AND user_id=@userId;
            IF @@ROWCOUNT=0
                INSERT INTO dbo.case_assignments (case_id,user_id,assignment_role,case_role,assigned_by_user_id)
                SELECT @caseId,@userId,@role,@caseRole,@actor
                WHERE EXISTS (SELECT 1 FROM dbo.app_users WHERE id=@userId AND is_active=1);
            """;
        AddAssignmentParameters(command, request, actorUserId);
        if (await command.ExecuteNonQueryAsync(token) == 0) throw new InvalidOperationException("The selected user is not active or the case does not exist.");
        await AuditAsync(connection, transaction, request.CaseId, actorUserId, "CaseAssignmentSaved", request.UserId.ToString(), JsonSerializer.Serialize(new { request.AssignmentRole, request.CaseRole }), token);
        await transaction.CommitAsync(token);
        return (await GetAssignmentsAsync(request.CaseId, request.UserId, token)).Single();
    }

    public async Task<bool> RevokeAssignmentAsync(long caseId, Guid userId, Guid actorUserId, CancellationToken token = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM dbo.case_assignments WHERE case_id=@caseId AND user_id=@userId";
        command.Parameters.Add(new SqlParameter("@caseId", caseId));
        command.Parameters.Add(new SqlParameter("@userId", userId));
        var removed = await command.ExecuteNonQueryAsync(token) > 0;
        if (removed) await AuditAsync(connection, transaction, caseId, actorUserId, "CaseAssignmentRevoked", userId.ToString(), null, token);
        await transaction.CommitAsync(token);
        return removed;
    }

    public async Task<bool> SetUserActiveAsync(Guid userId, bool active, Guid actorUserId, CancellationToken token = default)
    {
        if (userId == actorUserId && !active) throw new ArgumentException("Administrators cannot deactivate their own current account.");
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE dbo.app_users SET is_active=@active,updated_utc=SYSUTCDATETIME() WHERE id=@id";
        command.Parameters.Add(new SqlParameter("@active", active));
        command.Parameters.Add(new SqlParameter("@id", userId));
        var changed = await command.ExecuteNonQueryAsync(token) > 0;
        if (changed) await AuditAsync(connection, transaction, null, actorUserId, active ? "UserActivated" : "UserDeactivated", userId.ToString(), null, token);
        await transaction.CommitAsync(token);
        return changed;
    }

    private static void AddAssignmentParameters(System.Data.Common.DbCommand command, SaveCaseAssignmentRequest request, Guid actor)
    {
        command.Parameters.Add(new SqlParameter("@caseId", request.CaseId));
        command.Parameters.Add(new SqlParameter("@userId", request.UserId));
        command.Parameters.Add(new SqlParameter("@role", request.AssignmentRole));
        command.Parameters.Add(new SqlParameter("@caseRole", request.CaseRole));
        command.Parameters.Add(new SqlParameter("@actor", actor));
    }

    private static async Task AuditAsync(System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction transaction, long? caseId, Guid actor, string action, string entityId, string? details, CancellationToken token)
    {
        await using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = "INSERT INTO dbo.audit_events (case_id,actor_user_id,action,entity_type,entity_id,details_json) VALUES (@caseId,@actor,@action,'CaseAssignment',@entityId,@details)";
        audit.Parameters.Add(new SqlParameter("@caseId", (object?)caseId ?? DBNull.Value));
        audit.Parameters.Add(new SqlParameter("@actor", actor));
        audit.Parameters.Add(new SqlParameter("@action", action));
        audit.Parameters.Add(new SqlParameter("@entityId", entityId));
        audit.Parameters.Add(new SqlParameter("@details", (object?)details ?? DBNull.Value));
        await audit.ExecuteNonQueryAsync(token);
    }
}
