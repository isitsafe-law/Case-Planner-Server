using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// Test-build feedback batch (8 independent field/feature additions) - the 9 new plain CaseRecord
// fields added across items 2/3 (attorney fees), 5 (judge/division), 6 (FAP/parcel numbers), 7
// (case style), 8 (opposing counsel contact), and 9 (case folder path). Zero auth/identity
// dependency, so like CaseDispositionFieldsTests (the sibling precedent for this same "plain
// data-capture field" shape) this only exercises the SQLite path; the SQL Server migrations
// (046-051) and SqlServerCaseCatalogReader/CaseRecordDataMapper changes are reviewed for
// consistency with sibling files but not exercised against a live SQL Server here, same limitation
// already noted for every other migration in this repo.
public sealed class TestBuildFeedbackBatchFieldsTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task AllNineNewFieldsRoundTrip()
    {
        var saved = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "New Fields Batch Case",
            CaseNumber = "BATCH-1",
            County = "Pulaski",
            Status = "Closed",
            CaseStatus = "Resolved / Closed",
            Stage = "Resolved",
            Track = "Contested",
            AttorneyFeesAwarded = true,
            AttorneyFeesAmount = 4500.25m,
            Judge = "Hon. Jane Smith",
            Division = "Division 3",
            FapNumber = "FAP-2026-001",
            ParcelNumber = "PARCEL-55-102",
            CaseStyle = "State of Arkansas ex rel. Arkansas State Highway Commission v. John Doe, et al.\nSecond line of caption",
            OpposingCounselContact = "Jane Attorney\n555-123-4567\njane@example.com",
            CaseFolderPath = @"\\fileserver\share\JOB-1\TRACT-1",
        });

        Assert.True(saved.AttorneyFeesAwarded);
        Assert.Equal(4500.25m, saved.AttorneyFeesAmount);
        Assert.Equal("Hon. Jane Smith", saved.Judge);
        Assert.Equal("Division 3", saved.Division);
        Assert.Equal("FAP-2026-001", saved.FapNumber);
        Assert.Equal("PARCEL-55-102", saved.ParcelNumber);
        Assert.Contains("Second line of caption", saved.CaseStyle);
        Assert.Contains("jane@example.com", saved.OpposingCounselContact);
        Assert.Equal(@"\\fileserver\share\JOB-1\TRACT-1", saved.CaseFolderPath);

        var reloaded = await _fixture.Repository.GetCasesAsync("", "", "", "", true);
        var match = Assert.Single(reloaded, c => c.Id == saved.Id);
        Assert.True(match.AttorneyFeesAwarded);
        Assert.Equal(4500.25m, match.AttorneyFeesAmount);
        Assert.Equal("Hon. Jane Smith", match.Judge);
        Assert.Equal("Division 3", match.Division);
        Assert.Equal("FAP-2026-001", match.FapNumber);
        Assert.Equal("PARCEL-55-102", match.ParcelNumber);
        Assert.Contains("Second line of caption", match.CaseStyle);
        Assert.Contains("jane@example.com", match.OpposingCounselContact);
        Assert.Equal(@"\\fileserver\share\JOB-1\TRACT-1", match.CaseFolderPath);
    }

    [Fact]
    public async Task NewFieldsStayAtDefaultsWhenNotSupplied()
    {
        var saved = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "No New Fields Yet Case",
            CaseNumber = "BATCH-2",
            County = "Pulaski",
            Status = "Active",
            CaseStatus = "Active Litigation",
            Stage = "Discovery & Evaluation",
            Track = "Contested",
        });

        Assert.False(saved.AttorneyFeesAwarded);
        Assert.Null(saved.AttorneyFeesAmount);
        Assert.Null(saved.Judge);
        Assert.Null(saved.Division);
        Assert.Null(saved.FapNumber);
        Assert.Null(saved.ParcelNumber);
        Assert.Null(saved.CaseStyle);
        Assert.Null(saved.OpposingCounselContact);
        Assert.Null(saved.CaseFolderPath);
    }
}
