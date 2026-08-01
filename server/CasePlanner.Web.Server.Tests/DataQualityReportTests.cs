namespace CasePlanner.Web.Server.Tests;

public sealed class DataQualityReportTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task ReportIncludesStableChecksAndPortableTemplateCheck()
    {
        var report = await _fixture.Repository.GetDataQualityReportAsync();

        Assert.False(string.IsNullOrWhiteSpace(report.GeneratedAt));
        Assert.Contains(report.Issues, issue => issue.Key == "pipeline-unassigned");
        Assert.Contains(report.Issues, issue => issue.Key == "missing-case-style");
        Assert.Contains(report.Issues, issue => issue.Key == "missing-parties");
        Assert.Contains(report.Issues, issue => issue.Key == "duplicate-canonical-parties");
        Assert.Contains(report.Issues, issue => issue.Key == "jury-trial-conflict");
        Assert.Contains(report.Issues, issue => issue.Key == "jury-trial-event-missing");
        Assert.Contains(report.Issues, issue => issue.Key == "jury-trial-event-no-case-date");
        Assert.Contains(report.Issues, issue => issue.Key == "missing-template-files");
        Assert.Equal(0, report.Issues.Single(issue => issue.Key == "missing-template-files").Count);
        Assert.Equal(0, report.Issues.Single(issue => issue.Key == "invalid-document-template-files").Count);
        Assert.Equal(0, report.Issues.Single(issue => issue.Key == "unknown-document-template-tags").Count);
        Assert.Equal("Events", report.Issues.Single(issue => issue.Key == "jury-trial-conflict").Area);
        Assert.Equal("Documents", report.Issues.Single(issue => issue.Key == "missing-template-files").Area);
        Assert.DoesNotContain(report.Issues, issue => string.IsNullOrWhiteSpace(issue.Area));
        Assert.All(report.Issues, issue => Assert.Equal(Math.Max(0, issue.Count - issue.SampleCaseIds.Count), issue.AdditionalCaseCount));
    }

    [Fact]
    public async Task ReportFlagsAnActiveTemplateThatCannotBeOpened()
    {
        var template = (await _fixture.Repository.GetAllDocumentTemplatesForAdminAsync())
            .First(item => item.Template.IsBuiltin && item.ActiveVersion is not null);
        var path = template.ActiveVersion!.StoragePath;
        var original = await File.ReadAllBytesAsync(path);
        try
        {
            await File.WriteAllTextAsync(path, "not a docx package");
            var report = await _fixture.Repository.GetDataQualityReportAsync();
            Assert.True(report.Issues.Single(issue => issue.Key == "invalid-document-template-files").Count >= 1);
        }
        finally
        {
            await File.WriteAllBytesAsync(path, original);
        }
    }
}
