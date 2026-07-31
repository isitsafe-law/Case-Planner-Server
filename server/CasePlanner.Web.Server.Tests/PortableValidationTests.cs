using Xunit;

namespace CasePlanner.Web.Server.Tests;

public sealed class PortableValidationTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task BackupRestoreValidationCreatesAndReadsTemporaryCopy()
    {
        var report = await _fixture.Repository.ValidatePortableBackupAsync();

        Assert.True(report.Passed, string.Join(" | ", report.Checks.Select(x => $"{x.Name}: {x.Details}")));
        Assert.Contains(report.Checks, x => x.Name == "Create backup" && x.Passed);
        Assert.Contains(report.Checks, x => x.Name == "Backup integrity" && x.Passed);
        Assert.Contains(report.Checks, x => x.Name == "Restore/schema compatibility" && x.Passed);
        Assert.Contains(report.Checks, x => x.Name == "Restore read test" && x.Passed);
    }

    [Fact]
    public async Task ActualRestoreReturnsThePreRestoreSafetyBackupName()
    {
        var backup = await _fixture.Repository.CreateBackupNowAsync();

        var result = await _fixture.Repository.RestoreBackupAsync(backup.FileName);

        Assert.Equal(backup.FileName, result.RestoredFileName);
        Assert.False(string.IsNullOrWhiteSpace(result.SafetyBackupFileName));
        Assert.NotEqual(result.RestoredFileName, result.SafetyBackupFileName);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(_fixture.DatabasePath)!, "..", "backups", result.SafetyBackupFileName)));

        var diagnostics = await _fixture.Repository.GetDiagnosticsAsync();
        Assert.True(diagnostics.WriteSafetyOk, diagnostics.WriteSafetyMessage);
        Assert.True(diagnostics.CaseCount >= 4);
    }

    [Fact]
    public async Task DocumentGenerationFailureIsRetainedInDiagnostics()
    {
        await _fixture.Repository.RecordDocumentGenerationFailureAsync("request-123", "POST /api/cases/4/document-platform/templates/test/generate", "Template could not be opened.");

        var diagnostics = await _fixture.Repository.GetDiagnosticsAsync();
        Assert.NotNull(diagnostics.LastDocumentGenerationFailure);
        Assert.Equal("request-123", diagnostics.LastDocumentGenerationFailure!.RequestId);
        Assert.Contains("Template could not be opened", diagnostics.LastDocumentGenerationFailure.Message);
    }
}
