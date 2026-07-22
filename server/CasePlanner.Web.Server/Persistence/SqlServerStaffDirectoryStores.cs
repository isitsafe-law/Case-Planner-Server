using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

// SQL Server side of the Staff Directory (see StaffDirectoryStores.cs for why this exists).
// dbo.attorneys/dbo.legal_assistants (038_staff_directory.sql) are plain, non-case-scoped tables
// with no row_version/is_deleted columns, so unlike the litigation child-table stores this needs
// no optimistic concurrency or soft delete - it mirrors CasePlannerRepository's SQLite methods
// (GetAttorneysAsync/SaveAttorneyAsync/GetLegalAssistantsAsync/SaveLegalAssistantAsync) as closely
// as the two providers' schemas allow. There is no live SQL Server sandbox available here to
// exercise this against a real pilot instance - same caveat already noted for the rest of the
// dormant multi-user foundation.
public sealed class SqlServerAttorneyStore(IDatabaseConnectionFactory connections, IHttpContextAccessor accessor)
    : SqlServerWorkItemStoreBase(connections, accessor), IAttorneyStore
{
    public string Provider => "SqlServer";

    public async Task<List<AttorneyRecord>> GetAsync(CancellationToken token = default)
    {
        var result = new List<AttorneyRecord>();
        await using var connection = Connections.CreateConnection(); await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,name,title,is_active,sort_order,linked_user_id FROM dbo.attorneys ORDER BY sort_order,name";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            result.Add(new AttorneyRecord
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                Title = Text(reader, 2),
                IsActive = Bool(reader, 3),
                SortOrder = reader.GetInt32(4),
                LinkedUserId = Text(reader, 5),
            });
        }
        return result;
    }

    public async Task<AttorneyRecord> SaveAsync(AttorneyRecord model, CancellationToken token = default)
    {
        await using var connection = Connections.CreateConnection(); await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        if (model.Id == 0)
        {
            var sortOrder = model.SortOrder;
            if (sortOrder <= 0)
            {
                await using var maxCommand = connection.CreateCommand(); maxCommand.Transaction = transaction;
                maxCommand.CommandText = "SELECT COALESCE(MAX(sort_order),0)+1 FROM dbo.attorneys";
                sortOrder = Convert.ToInt32(await maxCommand.ExecuteScalarAsync(token));
            }
            model.SortOrder = sortOrder;
            command.CommandText = "INSERT INTO dbo.attorneys (name,title,is_active,sort_order,linked_user_id) OUTPUT INSERTED.id VALUES (@name,@title,@active,@sort,@linkedUserId)";
        }
        else
        {
            command.CommandText = "UPDATE dbo.attorneys SET name=@name,title=@title,is_active=@active,sort_order=@sort,linked_user_id=@linkedUserId OUTPUT INSERTED.id WHERE id=@id";
            command.Parameters.Add(new SqlParameter("@id", model.Id));
        }
        command.Parameters.Add(new SqlParameter("@name", model.Name));
        command.Parameters.Add(new SqlParameter("@title", Db(model.Title)));
        command.Parameters.Add(new SqlParameter("@active", model.IsActive));
        command.Parameters.Add(new SqlParameter("@sort", model.SortOrder));
        command.Parameters.Add(new SqlParameter("@linkedUserId", Db(model.LinkedUserId)));
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new InvalidOperationException($"Attorney {model.Id} was not found.");
            model.Id = reader.GetInt64(0);
        }
        await transaction.CommitAsync(token);
        return model;
    }
}

public sealed class SqlServerLegalAssistantStore(IDatabaseConnectionFactory connections, IHttpContextAccessor accessor)
    : SqlServerWorkItemStoreBase(connections, accessor), ILegalAssistantStore
{
    public string Provider => "SqlServer";

    public async Task<List<LegalAssistantRecord>> GetAsync(CancellationToken token = default)
    {
        var result = new List<LegalAssistantRecord>();
        await using var connection = Connections.CreateConnection(); await connection.OpenAsync(token);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id,name,is_active,sort_order,linked_user_id FROM dbo.legal_assistants ORDER BY sort_order,name";
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                result.Add(new LegalAssistantRecord
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    IsActive = Bool(reader, 2),
                    SortOrder = reader.GetInt32(3),
                    LinkedUserId = Text(reader, 4),
                });
            }
        }

        await using (var tiesCommand = connection.CreateCommand())
        {
            tiesCommand.CommandText = """
                SELECT laa.legal_assistant_id, a.id, a.name
                FROM dbo.legal_assistant_attorneys laa JOIN dbo.attorneys a ON a.id = laa.attorney_id
                ORDER BY a.sort_order, a.name
                """;
            await using var tiesReader = await tiesCommand.ExecuteReaderAsync(token);
            while (await tiesReader.ReadAsync(token))
            {
                var legalAssistantId = tiesReader.GetInt64(0);
                var match = result.FirstOrDefault(la => la.Id == legalAssistantId);
                if (match is null) continue;
                match.AttorneyIds.Add(tiesReader.GetInt64(1));
                match.AttorneyNames.Add(tiesReader.GetString(2));
            }
        }
        return result;
    }

    public async Task<LegalAssistantRecord> SaveAsync(LegalAssistantRecord model, CancellationToken token = default)
    {
        await using var connection = Connections.CreateConnection(); await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            if (model.Id == 0)
            {
                var sortOrder = model.SortOrder;
                if (sortOrder <= 0)
                {
                    await using var maxCommand = connection.CreateCommand(); maxCommand.Transaction = transaction;
                    maxCommand.CommandText = "SELECT COALESCE(MAX(sort_order),0)+1 FROM dbo.legal_assistants";
                    sortOrder = Convert.ToInt32(await maxCommand.ExecuteScalarAsync(token));
                }
                model.SortOrder = sortOrder;
                command.CommandText = "INSERT INTO dbo.legal_assistants (name,is_active,sort_order,linked_user_id) OUTPUT INSERTED.id VALUES (@name,@active,@sort,@linkedUserId)";
            }
            else
            {
                command.CommandText = "UPDATE dbo.legal_assistants SET name=@name,is_active=@active,sort_order=@sort,linked_user_id=@linkedUserId OUTPUT INSERTED.id WHERE id=@id";
                command.Parameters.Add(new SqlParameter("@id", model.Id));
            }
            command.Parameters.Add(new SqlParameter("@name", model.Name));
            command.Parameters.Add(new SqlParameter("@active", model.IsActive));
            command.Parameters.Add(new SqlParameter("@sort", model.SortOrder));
            command.Parameters.Add(new SqlParameter("@linkedUserId", Db(model.LinkedUserId)));
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new InvalidOperationException($"Legal assistant {model.Id} was not found.");
            model.Id = reader.GetInt64(0);
        }

        // Full replace, not a partial merge - matches SqliteLegalAssistantStore's SaveLegalAssistantAsync:
        // reassigning a legal assistant to different attorneys drops every prior tie, not just adds new ones.
        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM dbo.legal_assistant_attorneys WHERE legal_assistant_id=@id";
            deleteCommand.Parameters.Add(new SqlParameter("@id", model.Id));
            await deleteCommand.ExecuteNonQueryAsync(token);
        }

        model.AttorneyNames.Clear();
        foreach (var attorneyId in model.AttorneyIds.Distinct())
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = "INSERT INTO dbo.legal_assistant_attorneys (legal_assistant_id,attorney_id) VALUES (@laId,@attorneyId)";
            insertCommand.Parameters.Add(new SqlParameter("@laId", model.Id));
            insertCommand.Parameters.Add(new SqlParameter("@attorneyId", attorneyId));
            await insertCommand.ExecuteNonQueryAsync(token);
        }

        await transaction.CommitAsync(token);
        return model;
    }
}
