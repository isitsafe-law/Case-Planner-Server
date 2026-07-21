using System.Text;
using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// Multi-user rollout Phase 5 (reporting), data-capture track: FinalJudgmentAmount, DispositionType,
// TakingType, District. Zero auth/identity dependency (plain case fields, not roster/assignment
// dependent), so unlike several other multi-user phases these get full, normal dual-provider
// parity - this file only exercises the SQLite path (matching HearingStatusAndCaseTrialFieldsTests'
// precedent for the sibling trial_end_date/property_description addition); the SQL Server migration
// (037_case_disposition_fields.sql) and SqlServerCaseCatalogReader/SqlServerCaseImportService
// changes are reviewed for consistency with sibling files but not exercised against a live SQL
// Server here, same limitation already noted for every other migration in this repo.
public sealed class CaseDispositionFieldsTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task FinalJudgmentAmountDispositionTakingTypeAndDistrictRoundTrip()
    {
        var saved = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Disposition Fields Case",
            CaseNumber = "DISPOSITION-1",
            County = "Pulaski",
            Status = "Closed",
            CaseStatus = "Resolved / Closed",
            Stage = "Resolved",
            Track = "Contested",
            ClosedDate = "2026-07-20",
            FinalJudgmentAmount = 125000.50m,
            DispositionType = "Jury Trial",
            TakingType = "Partial",
            District = "District 6",
        });

        Assert.Equal(125000.50m, saved.FinalJudgmentAmount);
        Assert.Equal("Jury Trial", saved.DispositionType);
        Assert.Equal("Partial", saved.TakingType);
        Assert.Equal("District 6", saved.District);

        var reloaded = await _fixture.Repository.GetCasesAsync("", "", "", "", true);
        var match = Assert.Single(reloaded, c => c.Id == saved.Id);
        Assert.Equal(125000.50m, match.FinalJudgmentAmount);
        Assert.Equal("Jury Trial", match.DispositionType);
        Assert.Equal("Partial", match.TakingType);
        Assert.Equal("District 6", match.District);
    }

    [Fact]
    public async Task NewFieldsStayNullWhenNotSupplied()
    {
        var saved = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "No Disposition Yet Case",
            CaseNumber = "DISPOSITION-2",
            County = "Pulaski",
            Status = "Active",
            CaseStatus = "Active Litigation",
            Stage = "Discovery & Evaluation",
            Track = "Contested",
        });

        Assert.Null(saved.FinalJudgmentAmount);
        Assert.Null(saved.DispositionType);
        Assert.Null(saved.TakingType);
        Assert.Null(saved.District);
    }

    [Fact]
    public async Task CsvImportMapsDistrictTakingTypeDispositionTypeAndFinalJudgmentAmount()
    {
        const string csv = "Case Number,Case Name,Job Number,Tract,County,Status,District,Taking Type,Disposition Type,Final Judgment Amount\n" +
            "IMPORT-DISP-1,Import Disposition Case,JOB-IMPORT-1,TRACT-1,Pulaski,Closed,District 6,Full,Settlement,\"$42,500.00\"\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var result = await _fixture.Repository.ImportCasesCsvAsync(stream);

        Assert.Equal(1, result.Created);
        var cases = await _fixture.Repository.GetCasesAsync("", "", "", "", true);
        var match = Assert.Single(cases, c => c.CaseNumber == "IMPORT-DISP-1");
        Assert.Equal("District 6", match.District);
        Assert.Equal("Full", match.TakingType);
        Assert.Equal("Settlement", match.DispositionType);
        Assert.Equal(42500.00m, match.FinalJudgmentAmount);
    }
}
