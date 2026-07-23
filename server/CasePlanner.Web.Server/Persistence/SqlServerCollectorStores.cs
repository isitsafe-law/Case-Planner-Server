using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

// SQL Server side of the County Tax Collector reference lookup (see CollectorStores.cs for why
// this exists). dbo.collectors (055_collectors.sql) is a plain, non-case-scoped table with no
// row_version/is_deleted columns, so like SqlServerCircuitClerkStore this needs no optimistic
// concurrency or soft delete - it mirrors CasePlannerRepository's SQLite methods
// (GetCollectorsAsync/SaveCollectorAsync) as closely as the two providers' schemas allow. Unlike
// Assessor/CircuitClerk, [name] is nullable here (Lafayette/Searcy have no published collector
// name). There is no live SQL Server sandbox available here to exercise this against a real pilot
// instance - same caveat already noted for the rest of the dormant multi-user foundation.
public sealed class SqlServerCollectorStore(IDatabaseConnectionFactory connections, IHttpContextAccessor accessor)
    : SqlServerWorkItemStoreBase(connections, accessor), ICollectorStore
{
    public string Provider => "SqlServer";

    public async Task<List<CollectorRecord>> GetAsync(CancellationToken token = default)
    {
        var result = new List<CollectorRecord>();
        await using var connection = Connections.CreateConnection(); await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,county,name,address,phone,notes FROM dbo.collectors ORDER BY county";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            result.Add(new CollectorRecord
            {
                Id = reader.GetInt64(0),
                County = reader.GetString(1),
                Name = Text(reader, 2),
                Address = Text(reader, 3),
                Phone = Text(reader, 4),
                Notes = Text(reader, 5),
            });
        }
        return result;
    }

    // Keyed by County (the natural, stable lookup key the Settings-panel edit endpoint - PUT
    // /api/collectors/{county} - actually addresses), not by Id: every county row already exists
    // from the initial seed, so an edit here is always effectively an update, but this still
    // resolves Id from County first so a caller never has to know the row's Id up front - mirrors
    // SqliteCollectorStore/CasePlannerRepository.SaveCollectorAsync exactly.
    public async Task<CollectorRecord> SaveAsync(CollectorRecord model, CancellationToken token = default)
    {
        await using var connection = Connections.CreateConnection(); await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);

        if (model.Id == 0)
        {
            await using var lookupCommand = connection.CreateCommand();
            lookupCommand.Transaction = transaction;
            lookupCommand.CommandText = "SELECT id FROM dbo.collectors WHERE county=@county";
            lookupCommand.Parameters.Add(new SqlParameter("@county", model.County));
            var existingId = await lookupCommand.ExecuteScalarAsync(token);
            if (existingId is not null && existingId is not DBNull) model.Id = Convert.ToInt64(existingId);
        }

        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        if (model.Id == 0)
        {
            command.CommandText = "INSERT INTO dbo.collectors (county,name,address,phone,notes) OUTPUT INSERTED.id VALUES (@county,@name,@address,@phone,@notes)";
        }
        else
        {
            command.CommandText = "UPDATE dbo.collectors SET county=@county,name=@name,address=@address,phone=@phone,notes=@notes OUTPUT INSERTED.id WHERE id=@id";
            command.Parameters.Add(new SqlParameter("@id", model.Id));
        }
        command.Parameters.Add(new SqlParameter("@county", model.County));
        command.Parameters.Add(new SqlParameter("@name", Db(model.Name)));
        command.Parameters.Add(new SqlParameter("@address", Db(model.Address)));
        command.Parameters.Add(new SqlParameter("@phone", Db(model.Phone)));
        command.Parameters.Add(new SqlParameter("@notes", Db(model.Notes)));
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new InvalidOperationException($"Collector {model.Id} was not found.");
            model.Id = reader.GetInt64(0);
        }
        await transaction.CommitAsync(token);
        return model;
    }
}
