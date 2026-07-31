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
        Assert.Contains(report.Issues, issue => issue.Key == "jury-trial-conflict");
        Assert.Contains(report.Issues, issue => issue.Key == "jury-trial-event-missing");
        Assert.Contains(report.Issues, issue => issue.Key == "jury-trial-event-no-case-date");
        Assert.Contains(report.Issues, issue => issue.Key == "missing-template-files");
        Assert.Equal(0, report.Issues.Single(issue => issue.Key == "missing-template-files").Count);
        Assert.Equal(0, report.Issues.Single(issue => issue.Key == "unknown-document-template-tags").Count);
        Assert.All(report.Issues, issue => Assert.Equal(Math.Max(0, issue.Count - issue.SampleCaseIds.Count), issue.AdditionalCaseCount));
    }
}
