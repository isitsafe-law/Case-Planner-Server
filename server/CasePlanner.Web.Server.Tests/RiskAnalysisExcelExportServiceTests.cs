using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;

namespace CasePlanner.Web.Server.Tests;

// Structural sanity check for the .xlsx export - confirms it builds without throwing and the
// result is a real, openable workbook with the expected fixed-row ledger. Formula correctness is
// verified manually during development via the xlsx skill's recalc.py (LibreOffice), not here -
// there's no LibreOffice/Excel available at test-run time to actually recalculate formulas.
public class RiskAnalysisExcelExportServiceTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task BuildWorkbook_ProducesOpenableXlsx_WithLedgerAndOldOffers()
    {
        var c = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Fixture Landowner et al.",
            CaseNumber = "12CV-26-1",
            County = "Pulaski",
            Status = "Active",
            Track = "Contested",
            FilingDate = "2026-01-15",
            DepositAmount = 100000m,
            AssignedAttorney = "Cody E.",
            Appraiser = "ASHC Appraiser",
            LandownerAppraiserName = "LO Appraiser",
        });

        await _fixture.Repository.SaveRiskAnalysisAsync(new RiskAnalysisInput
        {
            CaseId = c.Id,
            Narrative = "Test narrative text.",
            Rows =
            [
                new RiskAnalysisRowInput { RowKey = "LandownerOpinionOfValue", Label = "LANDOWNER'S OPINION OF VALUE", OfferMaker = "Landowner", JustCompensation = 150000m },
                new RiskAnalysisRowInput { RowKey = "LandownerAppraisal", Label = "LANDOWNER'S APPRAISAL", OfferMaker = "Landowner", JustCompensation = 140000m },
                new RiskAnalysisRowInput { RowKey = "AshcFirstOffer", Label = "ASHC FIRST OFFER", OfferMaker = "ASHC", IncludeSplit = true, JustCompensation = 105000m },
                new RiskAnalysisRowInput { RowKey = "AshcCounteroffer", Label = "LANDOWNER'S COUNTEROFFER", OfferMaker = "Landowner", IncludeSplit = true, JustCompensation = 130000m },
                new RiskAnalysisRowInput { RowKey = "LandownerCounteroffer", Label = "ASHC COUNTEROFFER", OfferMaker = "ASHC", IncludeSplit = true, JustCompensation = 112000m },
            ],
        });

        await _fixture.Repository.SaveOfferLogEntryAsync(new RiskAnalysisOfferLogEntry
        {
            CaseId = c.Id,
            OfferDate = "2025-11-01",
            Party = "ASHC",
            Amount = 98000m,
        });

        var analysis = await _fixture.Repository.GetRiskAnalysisAsync(c.Id);
        var offerLog = await _fixture.Repository.GetOfferLogAsync(c.Id);

        var bytes = RiskAnalysisExcelExportService.BuildWorkbook(c, analysis, offerLog);

        Assert.NotEmpty(bytes);

        // FullCalculationOnLoad is set by a raw-OpenXml post-process step ClosedXML doesn't
        // expose - verify it landed via the raw package APIs.
        using (var calcStream = new MemoryStream(bytes))
        using (var doc = SpreadsheetDocument.Open(calcStream, false))
        {
            Assert.True(doc.WorkbookPart!.Workbook.CalculationProperties?.FullCalculationOnLoad?.Value);
        }

        // Structural/content assertions via ClosedXML (the same library that wrote the file),
        // matching how the app itself reads .xlsx files (ImportCasesXlsxAsync).
        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);
        var ws = workbook.Worksheet(1);

        Assert.Equal("Fixture Landowner et al.", ws.Cell("B1").GetString());
        Assert.Equal("12CV-26-1", ws.Cell("B2").GetString());
        Assert.Equal("ASHC FIRST OFFER", ws.Cell("A17").GetString());
        Assert.True(ws.Cell("C14").HasFormula);
        Assert.True(ws.Cell("B18").HasFormula);
        Assert.Equal("OLD OFFERS (LIST BELOW)", ws.Cell("A27").GetString());
        Assert.Equal("ASHC", ws.Cell("B29").GetString());
        Assert.Equal(98000m, ws.Cell("C29").GetValue<decimal>());
    }
}
