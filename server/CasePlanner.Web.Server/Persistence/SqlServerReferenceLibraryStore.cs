using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

public sealed class SqlServerReferenceLibraryStore(IDatabaseConnectionFactory connections) : IReferenceLibraryStore
{
    public string Provider => "SqlServer";

    public async Task<List<ReferenceDocument>> GetAsync(CancellationToken token = default)
    {
        var result = new List<ReferenceDocument>();
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT document_key,title,description,document_text FROM dbo.reference_library_documents WHERE is_deleted=0 ORDER BY title,document_key";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            result.Add(new ReferenceDocument
            {
                Key = reader.GetString(0),
                Title = reader.GetString(1),
                Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Text = reader.IsDBNull(3) ? "" : reader.GetString(3)
            });
        }
        return result;
    }

    public async Task<ReferenceDocument> SaveAsync(ReferenceDocumentUpdate model, CancellationToken token = default)
    {
        Validate(model);
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            MERGE dbo.reference_library_documents AS target
            USING (SELECT @key AS document_key) AS source ON target.document_key=source.document_key
            WHEN MATCHED THEN UPDATE SET title=@title,description=@description,document_text=@text,is_deleted=0,updated_utc=SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT(document_key,title,description,document_text) VALUES(@key,@title,@description,@text);
            """;
        command.Parameters.Add(new SqlParameter("@key", model.Key.Trim()));
        command.Parameters.Add(new SqlParameter("@title", model.Title.Trim()));
        command.Parameters.Add(new SqlParameter("@description", (object?)model.Description?.Trim() ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@text", model.Text ?? ""));
        await command.ExecuteNonQueryAsync(token);
        return new ReferenceDocument { Key = model.Key.Trim(), Title = model.Title.Trim(), Description = model.Description?.Trim() ?? "", Text = model.Text ?? "" };
    }

    public async Task DeleteAsync(string key, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Reference document key is required.");
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.reference_library_documents SET is_deleted=1,updated_utc=SYSUTCDATETIME() WHERE document_key=@key AND is_deleted=0";
        command.Parameters.Add(new SqlParameter("@key", key.Trim()));
        await command.ExecuteNonQueryAsync(token);
    }

    private static void Validate(ReferenceDocumentUpdate model)
    {
        if (string.IsNullOrWhiteSpace(model.Key) || model.Key.Length > 80 || model.Key.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '-')))
            throw new ArgumentException("Reference document key must contain only letters, numbers, hyphens, and underscores.");
        if (string.IsNullOrWhiteSpace(model.Title)) throw new ArgumentException("Reference document title is required.");
    }
}
