using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

// SQL Server side of the Newspaper reference lookup (see NewspaperStores.cs for why this exists).
// dbo.newspapers (065_newspapers.sql) is a plain, non-case-scoped table with no row_version/
// is_deleted columns, so like SqlServerCollectorStore this needs no optimistic concurrency or soft
// delete. Unlike Collector/CircuitClerk/Assessor, there is no "resolve existing by county" lookup -
// County is not unique here, so every save addresses the row by Id directly (Id == 0 means insert).
// There is no live SQL Server sandbox available here to exercise this against a real pilot instance
// - same caveat already noted for the rest of the dormant multi-user foundation.
public sealed class SqlServerNewspaperStore(IDatabaseConnectionFactory connections, IHttpContextAccessor accessor)
    : SqlServerWorkItemStoreBase(connections, accessor), INewspaperStore
{
    public string Provider => "SqlServer";

    private const string Columns = "id,county,name,is_general_circulation,publication_days_frequency,submission_deadline,contact_name,phone,email,address,billing_affidavit_contact,typical_cost,notes,is_active";

    private static NewspaperRecord Read(System.Data.Common.DbDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        County = reader.GetString(1),
        Name = reader.GetString(2),
        IsGeneralCirculation = reader.GetBoolean(3),
        PublicationDaysFrequency = Text(reader, 4),
        SubmissionDeadline = Text(reader, 5),
        ContactName = Text(reader, 6),
        Phone = Text(reader, 7),
        Email = Text(reader, 8),
        Address = Text(reader, 9),
        BillingAffidavitContact = Text(reader, 10),
        TypicalCost = reader.IsDBNull(11) ? null : reader.GetDecimal(11),
        Notes = Text(reader, 12),
        IsActive = reader.GetBoolean(13),
    };

    public async Task<List<NewspaperRecord>> GetAsync(CancellationToken token = default)
    {
        var result = new List<NewspaperRecord>();
        await using var connection = Connections.CreateConnection(); await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM dbo.newspapers ORDER BY county,name";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(Read(reader));
        return result;
    }

    public async Task<NewspaperRecord> SaveAsync(NewspaperRecord model, CancellationToken token = default)
    {
        await using var connection = Connections.CreateConnection(); await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);

        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        if (model.Id == 0)
        {
            command.CommandText = """
                INSERT INTO dbo.newspapers (county,name,is_general_circulation,publication_days_frequency,submission_deadline,contact_name,phone,email,address,billing_affidavit_contact,typical_cost,notes,is_active)
                OUTPUT INSERTED.id
                VALUES (@county,@name,@general,@frequency,@deadline,@contactName,@phone,@email,@address,@billingContact,@cost,@notes,@active)
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE dbo.newspapers SET county=@county,name=@name,is_general_circulation=@general,publication_days_frequency=@frequency,
                    submission_deadline=@deadline,contact_name=@contactName,phone=@phone,email=@email,address=@address,
                    billing_affidavit_contact=@billingContact,typical_cost=@cost,notes=@notes,is_active=@active
                OUTPUT INSERTED.id
                WHERE id=@id
                """;
            command.Parameters.Add(new SqlParameter("@id", model.Id));
        }
        command.Parameters.Add(new SqlParameter("@county", model.County));
        command.Parameters.Add(new SqlParameter("@name", model.Name));
        command.Parameters.Add(new SqlParameter("@general", model.IsGeneralCirculation));
        command.Parameters.Add(new SqlParameter("@frequency", Db(model.PublicationDaysFrequency)));
        command.Parameters.Add(new SqlParameter("@deadline", Db(model.SubmissionDeadline)));
        command.Parameters.Add(new SqlParameter("@contactName", Db(model.ContactName)));
        command.Parameters.Add(new SqlParameter("@phone", Db(model.Phone)));
        command.Parameters.Add(new SqlParameter("@email", Db(model.Email)));
        command.Parameters.Add(new SqlParameter("@address", Db(model.Address)));
        command.Parameters.Add(new SqlParameter("@billingContact", Db(model.BillingAffidavitContact)));
        command.Parameters.Add(new SqlParameter("@cost", model.TypicalCost.HasValue ? model.TypicalCost.Value : DBNull.Value));
        command.Parameters.Add(new SqlParameter("@notes", Db(model.Notes)));
        command.Parameters.Add(new SqlParameter("@active", model.IsActive));
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new InvalidOperationException($"Newspaper {model.Id} was not found.");
            model.Id = reader.GetInt64(0);
        }
        await transaction.CommitAsync(token);
        return model;
    }
}
