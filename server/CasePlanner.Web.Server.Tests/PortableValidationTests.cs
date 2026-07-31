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
}
