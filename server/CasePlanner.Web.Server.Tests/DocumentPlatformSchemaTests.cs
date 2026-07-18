using Microsoft.Data.Sqlite;

namespace CasePlanner.Web.Server.Tests;

// Build-plan step 3 (data model cutover): proves the new SQLite tables actually get created by
// InitializeAsync and that their constraints behave as designed. No repository CRUD methods exist
// for these tables yet (deliberately deferred until the case-generation pipeline that will
// actually drive them is built) - this talks to the schema directly, the same throwaway SQLite
// file RepositoryTestFixture already sets up for every other test.
public sealed class DocumentPlatformSchemaTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection($"Data Source={_fixture.DatabasePath}");
        await connection.OpenAsync();
        return connection;
    }

    [Fact]
    public async Task AllDocumentPlatformTablesExist()
    {
        await using var connection = await OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'document_%'";
        var names = new HashSet<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        }

        Assert.Contains("document_templates", names);
        Assert.Contains("document_template_versions", names);
        Assert.Contains("document_runtime_inputs", names);
        Assert.Contains("document_template_sections", names);
        Assert.Contains("document_section_overlaps", names);
        Assert.Contains("document_generations", names);
    }

    [Fact]
    public async Task TemplateVersionAndSectionRowsRoundTrip()
    {
        await using var connection = await OpenAsync();

        var templateId = await ScalarAsync(connection,
            "INSERT INTO document_templates (template_key, title, category, is_builtin, created_at) " +
            "VALUES ('interrogatories', 'Interrogatories & Requests for Production', 'Discovery', 1, @now); " +
            "SELECT last_insert_rowid();",
            ("@now", DateTime.UtcNow.ToString("O")));

        var versionId = await ScalarAsync(connection,
            "INSERT INTO document_template_versions (template_id, version, storage_path, is_active, created_at) " +
            "VALUES (@templateId, 1, 'templates/documents/interrogatories_v1.docx', 1, @now); " +
            "SELECT last_insert_rowid();",
            ("@templateId", templateId), ("@now", DateTime.UtcNow.ToString("O")));

        await ExecuteAsync(connection,
            "INSERT INTO document_template_sections (template_version_id, section_key, label, issue_tag_name, sort_order) " +
            "VALUES (@versionId, 'Drainage', 'Drainage', 'Drainage', 1)",
            ("@versionId", versionId));

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT label FROM document_template_sections WHERE template_version_id = @v";
        cmd.Parameters.AddWithValue("@v", versionId);
        Assert.Equal("Drainage", await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task OverlapPairMustBeStoredWithSmallerIdFirst()
    {
        await using var connection = await OpenAsync();
        var templateId = await ScalarAsync(connection,
            "INSERT INTO document_templates (template_key, title, created_at) VALUES ('t', 'T', @now); SELECT last_insert_rowid();",
            ("@now", DateTime.UtcNow.ToString("O")));
        var versionId = await ScalarAsync(connection,
            "INSERT INTO document_template_versions (template_id, version, storage_path, created_at) VALUES (@t, 1, 'p', @now); SELECT last_insert_rowid();",
            ("@t", templateId), ("@now", DateTime.UtcNow.ToString("O")));
        var sectionA = await ScalarAsync(connection,
            "INSERT INTO document_template_sections (template_version_id, section_key, label) VALUES (@v, 'Drainage', 'Drainage'); SELECT last_insert_rowid();",
            ("@v", versionId));
        var sectionB = await ScalarAsync(connection,
            "INSERT INTO document_template_sections (template_version_id, section_key, label) VALUES (@v, 'Access', 'Access'); SELECT last_insert_rowid();",
            ("@v", versionId));

        var (smaller, larger) = sectionA < sectionB ? (sectionA, sectionB) : (sectionB, sectionA);

        // Correct order succeeds.
        await ExecuteAsync(connection,
            "INSERT INTO document_section_overlaps (section_a_id, section_b_id, note) VALUES (@a, @b, 'may overlap')",
            ("@a", smaller), ("@b", larger));

        // Reversed order violates the CHECK constraint rather than silently creating a duplicate
        // pairing the generation-time checklist would have to de-duplicate itself.
        var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO document_section_overlaps (section_a_id, section_b_id) VALUES (@a, @b)";
        cmd.Parameters.AddWithValue("@a", larger);
        cmd.Parameters.AddWithValue("@b", smaller);
        await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task GenerationRowRecordsWhichSectionsWereIncluded()
    {
        await using var connection = await OpenAsync();
        var templateId = await ScalarAsync(connection,
            "INSERT INTO document_templates (template_key, title, created_at) VALUES ('t', 'T', @now); SELECT last_insert_rowid();",
            ("@now", DateTime.UtcNow.ToString("O")));
        var versionId = await ScalarAsync(connection,
            "INSERT INTO document_template_versions (template_id, version, storage_path, created_at) VALUES (@t, 1, 'p', @now); SELECT last_insert_rowid();",
            ("@t", templateId), ("@now", DateTime.UtcNow.ToString("O")));

        await ExecuteAsync(connection,
            "INSERT INTO document_generations (case_id, template_id, template_version_id, output_path, rendered_at, sections_included_json, is_draft, is_finalized) " +
            "VALUES (1, @t, @v, 'exports/cases/1/out.docx', @now, '[\"Drainage\"]', 0, 1)",
            ("@t", templateId), ("@v", versionId), ("@now", DateTime.UtcNow.ToString("O")));

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT sections_included_json, is_finalized FROM document_generations WHERE template_version_id = @v";
        cmd.Parameters.AddWithValue("@v", versionId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("[\"Drainage\"]", reader.GetString(0));
        Assert.Equal(1L, reader.GetInt64(1));
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }
}
