using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// Batch 1 item 3: Generated Documents table gained a delete action for both merged sources -
// legacy document_exports rows and document_platform_generations (document_generations table)
// rows. These exercise the repository-level delete-with-file-cleanup for the legacy side; the
// platform-generation side is covered in DocumentPlatformGenerationTests.
public sealed class GeneratedDocumentDeletionTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<CaseRecord> CreateCaseAsync() => await _fixture.Repository.SaveCaseAsync(new CaseRecord
    {
        CaseName = "Export Case", CaseNumber = "23CV-900", County = "Pulaski", Status = "Active", Track = "Contested",
    });

    [Fact]
    public async Task DeletingADocumentExportRemovesTheRowAndTheStoredFile()
    {
        var caseRecord = await CreateCaseAsync();
        var export = await _fixture.Repository.GenerateDocumentAsync(caseRecord.Id, "summary");
        Assert.True(File.Exists(export.OutputPath));

        var deleted = await _fixture.Repository.DeleteDocumentExportAsync(export.Id);

        Assert.True(deleted);
        Assert.Null(await _fixture.Repository.GetDocumentExportByIdAsync(export.Id));
        Assert.False(File.Exists(export.OutputPath));
    }

    [Fact]
    public async Task DeletingADocumentExportThatDoesNotExistReturnsFalse()
    {
        var deleted = await _fixture.Repository.DeleteDocumentExportAsync(999_999);

        Assert.False(deleted);
    }

    [Fact]
    public async Task DeletingOneExportDoesNotAffectAnotherExportForTheSameCase()
    {
        var caseRecord = await CreateCaseAsync();
        var first = await _fixture.Repository.GenerateDocumentAsync(caseRecord.Id, "summary");
        var second = await _fixture.Repository.GenerateDocumentAsync(caseRecord.Id, "review");

        await _fixture.Repository.DeleteDocumentExportAsync(first.Id);

        Assert.Null(await _fixture.Repository.GetDocumentExportByIdAsync(first.Id));
        Assert.NotNull(await _fixture.Repository.GetDocumentExportByIdAsync(second.Id));
        Assert.True(File.Exists(second.OutputPath));
    }
}
