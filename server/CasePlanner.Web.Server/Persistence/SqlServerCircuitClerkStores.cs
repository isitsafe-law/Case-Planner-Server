using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

// SQL Server side of the Circuit Clerk reference lookup (see CircuitClerkStores.cs for why this
// exists). dbo.circuit_clerks (053_circuit_clerks.sql) is a plain, non-case-scoped table with no
// row_version/is_deleted columns, so like SqlServerAttorneyStore this needs no optimistic
// concurrency or soft delete - it mirrors CasePlannerRepository's SQLite methods
// (GetCircuitClerksAsync/SaveCircuitClerkAsync) as closely as the two providers' schemas allow.
// There is no live SQL Server sandbox available here to exercise this against a real pilot
// instance - same caveat already noted for the rest of the dormant multi-user foundation.
public sealed class SqlServerCircuitClerkStore(IDatabaseConnectionFactory connections, IHttpContextAccessor accessor)
    : SqlServerWorkItemStoreBase(connections, accessor), ICircuitClerkStore
{
    public string Provider => "SqlServer";

    public async Task<List<CircuitClerkRecord>> GetAsync(CancellationToken token = default)
    {
        var result = new List<CircuitClerkRecord>();
        await using var connection = Connections.CreateConnection(); await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,county,clerk_name,address,phone,notes FROM dbo.circuit_clerks ORDER BY county";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            result.Add(new CircuitClerkRecord
            {
                Id = reader.GetInt64(0),
                County = reader.GetString(1),
                ClerkName = reader.GetString(2),
                Address = Text(reader, 3),
                Phone = Text(reader, 4),
                Notes = Text(reader, 5),
            });
        }
        return result;
    }

    // Keyed by County (the natural, stable lookup key the Settings-panel edit endpoint - PUT
    // /api/circuit-clerks/{county} - actually addresses), not by Id: every county row already
    // exists from the initial seed, so an edit here is always effectively an update, but this
    // still resolves Id from County first so a caller never has to know the row's Id up front -
    // mirrors SqliteCircuitClerkStore/CasePlannerRepository.SaveCircuitClerkAsync exactly.
    public async Task<CircuitClerkRecord> SaveAsync(CircuitClerkRecord model, CancellationToken token = default)
    {
        await using var connection = Connections.CreateConnection(); await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);

        if (model.Id == 0)
        {
            await using var lookupCommand = connection.CreateCommand();
            lookupCommand.Transaction = transaction;
            lookupCommand.CommandText = "SELECT id FROM dbo.circuit_clerks WHERE county=@county";
            lookupCommand.Parameters.Add(new SqlParameter("@county", model.County));
            var existingId = await lookupCommand.ExecuteScalarAsync(token);
            if (existingId is not null && existingId is not DBNull) model.Id = Convert.ToInt64(existingId);
        }

        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        if (model.Id == 0)
        {
            command.CommandText = "INSERT INTO dbo.circuit_clerks (county,clerk_name,address,phone,notes) OUTPUT INSERTED.id VALUES (@county,@clerkName,@address,@phone,@notes)";
        }
        else
        {
            command.CommandText = "UPDATE dbo.circuit_clerks SET county=@county,clerk_name=@clerkName,address=@address,phone=@phone,notes=@notes OUTPUT INSERTED.id WHERE id=@id";
            command.Parameters.Add(new SqlParameter("@id", model.Id));
        }
        command.Parameters.Add(new SqlParameter("@county", model.County));
        command.Parameters.Add(new SqlParameter("@clerkName", model.ClerkName));
        command.Parameters.Add(new SqlParameter("@address", Db(model.Address)));
        command.Parameters.Add(new SqlParameter("@phone", Db(model.Phone)));
        command.Parameters.Add(new SqlParameter("@notes", Db(model.Notes)));
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new InvalidOperationException($"Circuit clerk {model.Id} was not found.");
            model.Id = reader.GetInt64(0);
        }
        await transaction.CommitAsync(token);
        return model;
    }
}
