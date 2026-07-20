using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Persistence;
using CasePlanner.Web.Server.Services;
using Microsoft.Data.Sqlite;

namespace CasePlanner.Web.Server.Tests;

// Regression test for the notes-export timestamp bug: CreatedAt/UpdatedAt are stored as UTC ISO
// 8601 strings (SaveCaseNoteAsync writes DateTime.UtcNow.ToString("O")), but the export's Display
// helper used to pass that raw string straight through instead of converting it to local time like
// the "Generated:" line does. SaveCaseNoteAsync always stamps CreatedAt with "now" on insert, so to
// pin an exact, known UTC instant this writes directly to the same throwaway SQLite file
// RepositoryTestFixture sets up (mirroring the direct-SQL approach used in
// DocumentPlatformSchemaTests), then exercises the real ProviderNeutralCaseNotesExportService
// end-to-end against a real FileSystemDocumentStorage and reads the exported file back.
public sealed class CaseNotesExportServiceTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task ExportAsync_ConvertsStoredUtcTimestampsToLocalTime()
    {
        var c = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Timestamp Test Case",
            CaseNumber = "26CV-TS-1",
        });

        const string utcCreated = "2026-07-20T14:03:48.7528185Z";
        const string utcUpdated = "2026-07-21T09:15:00.0000000Z";

        await using (var connection = new SqliteConnection($"Data Source={_fixture.DatabasePath}"))
        {
            await connection.OpenAsync();
            var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO case_notes (case_id, title, body, created_at, updated_at)
                VALUES (@case_id, @title, @body, @created_at, @updated_at);
                """;
            cmd.Parameters.AddWithValue("@case_id", c.Id);
            cmd.Parameters.AddWithValue("@title", "Timestamp Note");
            cmd.Parameters.AddWithValue("@body", "Body text.");
            cmd.Parameters.AddWithValue("@created_at", utcCreated);
            cmd.Parameters.AddWithValue("@updated_at", utcUpdated);
            await cmd.ExecuteNonQueryAsync();
        }

        var contentRoot = Path.Combine(Path.GetTempPath(), "cpw_notes_export_" + Guid.NewGuid().ToString("N"), "server", "CasePlanner.Web.Server");
        Directory.CreateDirectory(contentRoot);
        try
        {
            var paths = new PathService(new TestHostEnvironment(contentRoot));
            var documents = new FileSystemDocumentStorage(paths);
            var workspace = new SqliteOperationalWorkspaceQuery(_fixture.Repository);
            var service = new ProviderNeutralCaseNotesExportService(workspace, documents);

            var result = await service.ExportAsync(c.Id);
            var text = await documents.ReadTextAsync(result.OutputPath);
            Assert.NotNull(text);

            var expectedCreated = DateTime.Parse(utcCreated, null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime().ToString("G");
            var expectedUpdated = DateTime.Parse(utcUpdated, null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime().ToString("G");

            Assert.Contains($"Created: {expectedCreated}", text);
            Assert.Contains($"Last Updated: {expectedUpdated}", text);
            Assert.DoesNotContain(utcCreated, text);
            Assert.DoesNotContain(utcUpdated, text);
            Assert.NotEqual(utcCreated, expectedCreated);
        }
        finally
        {
            var root = Path.GetFullPath(Path.Combine(contentRoot, "..", ".."));
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExportAsync_ShowsNotRecorded_ForBlankTimestamps()
    {
        var c = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Blank Timestamp Case",
            CaseNumber = "26CV-TS-2",
        });

        await using (var connection = new SqliteConnection($"Data Source={_fixture.DatabasePath}"))
        {
            await connection.OpenAsync();
            var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO case_notes (case_id, title, body, created_at, updated_at)
                VALUES (@case_id, @title, @body, '', '');
                """;
            cmd.Parameters.AddWithValue("@case_id", c.Id);
            cmd.Parameters.AddWithValue("@title", "Blank Note");
            cmd.Parameters.AddWithValue("@body", "Body text.");
            await cmd.ExecuteNonQueryAsync();
        }

        var contentRoot = Path.Combine(Path.GetTempPath(), "cpw_notes_export_" + Guid.NewGuid().ToString("N"), "server", "CasePlanner.Web.Server");
        Directory.CreateDirectory(contentRoot);
        try
        {
            var paths = new PathService(new TestHostEnvironment(contentRoot));
            var documents = new FileSystemDocumentStorage(paths);
            var workspace = new SqliteOperationalWorkspaceQuery(_fixture.Repository);
            var service = new ProviderNeutralCaseNotesExportService(workspace, documents);

            var result = await service.ExportAsync(c.Id);
            var text = await documents.ReadTextAsync(result.OutputPath);

            Assert.Contains("Created: Not recorded", text);
            Assert.Contains("Last Updated: Not recorded", text);
        }
        finally
        {
            var root = Path.GetFullPath(Path.Combine(contentRoot, "..", ".."));
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
