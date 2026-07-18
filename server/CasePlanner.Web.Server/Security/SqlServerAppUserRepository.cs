using CasePlanner.Data;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Security;

public sealed class SqlServerAppUserRepository(IDatabaseConnectionFactory connectionFactory)
{
    public async Task<AuthenticatedUserProfile> ProvisionAsync(EntraIdentity identity, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var id = await UpdateAsync(connection, identity, cancellationToken);
        if (id is null)
        {
            try { id = await InsertAsync(connection, identity, cancellationToken); }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                id = await UpdateAsync(connection, identity, cancellationToken);
            }
        }
        if (id is null) throw new InvalidOperationException("The authenticated Entra user could not be provisioned.");
        return new(id.Value, identity.TenantId, identity.ObjectId, identity.DisplayName, identity.Email, identity.Roles);
    }

    private static async Task<Guid?> UpdateAsync(System.Data.Common.DbConnection connection, EntraIdentity identity, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.app_users
            SET display_name=@name, email=@email, entra_tenant_id=@tenant, entra_object_id=@object,
                last_login_utc=SYSUTCDATETIME(), updated_utc=SYSUTCDATETIME()
            OUTPUT INSERTED.id
            WHERE external_subject=@subject AND is_active=1
            """;
        AddParameters(command, identity);
        var value = await command.ExecuteScalarAsync(token);
        return value is Guid id ? id : null;
    }

    private static async Task<Guid> InsertAsync(System.Data.Common.DbConnection connection, EntraIdentity identity, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.app_users (external_subject, entra_tenant_id, entra_object_id, display_name, email, last_login_utc)
            OUTPUT INSERTED.id
            VALUES (@subject,@tenant,@object,@name,@email,SYSUTCDATETIME())
            """;
        AddParameters(command, identity);
        return (Guid)(await command.ExecuteScalarAsync(token) ?? throw new InvalidOperationException("User insert returned no identifier."));
    }

    private static void AddParameters(System.Data.Common.DbCommand command, EntraIdentity identity)
    {
        command.Parameters.Add(new SqlParameter("@subject", identity.ExternalSubject));
        command.Parameters.Add(new SqlParameter("@tenant", Guid.Parse(identity.TenantId)));
        command.Parameters.Add(new SqlParameter("@object", Guid.Parse(identity.ObjectId)));
        command.Parameters.Add(new SqlParameter("@name", identity.DisplayName));
        command.Parameters.Add(new SqlParameter("@email", (object?)identity.Email ?? DBNull.Value));
    }
}
