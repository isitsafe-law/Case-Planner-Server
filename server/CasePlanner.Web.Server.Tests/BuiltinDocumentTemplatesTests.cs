using CasePlanner.Web.Server.Models;
using DocumentFormat.OpenXml.Packaging;

namespace CasePlanner.Web.Server.Tests;

// Build-plan step 6 (re-author remaining built-ins): Judgment (unified NoTaxes/TaxesOwed branch),
// Settlement Justification Memo, and Requests for Admission, generated end to end through the
// same platform pipeline DocumentPlatformGenerationTests proved out for Interrogatories.
public sealed class BuiltinDocumentTemplatesTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<CaseRecord> CreateJudgmentCaseAsync(decimal? taxOwedAmount = null)
    {
        var caseRecord = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Judgment Case",
            CaseNumber = "23CV-501",
            County = "Pulaski",
            Landowner = "Smith Family Trust",
            Status = "Active",
            CaseStatus = "Active Litigation",
            Track = "Contested",
            Tract = "7",
            JobNumber = "090554",
            DepositAmount = 15000m,
            AcquisitionAcres = 1.25m,
            TaxOwedAmount = taxOwedAmount,
        });

        var org = await _fixture.Repository.GetOrgDefaultsAsync();
        org.AttorneyName = "Jane Doe";
        org.BarNumber = "2020123";
        org.Phone = "501-555-0100";
        org.Email = "jane.doe@ardot.gov";
        org.AddressLine1 = "10324 Interstate 30";
        org.AddressLine2 = "Little Rock, AR 72209";
        await _fixture.Repository.SaveOrgDefaultsAsync(org);

        return caseRecord;
    }

    private static Dictionary<string, string> JudgmentInputs() => new()
    {
        ["TractDescription"] = "A tract of land in the SW 1/4 of Section 12.",
        ["TCEDescription"] = "A temporary construction easement 20 feet in width.",
        ["OwnershipDate"] = "2020-01-15",
        ["SummonsDate"] = "2023-02-01",
        ["WarningOrderNewspaper"] = "Arkansas Democrat-Gazette",
        ["WarningOrderDate1"] = "2023-02-10",
        ["WarningOrderDate2"] = "2023-02-17",
        ["JustCompensationAmount"] = "15000",
        ["JudgeSignatureDate"] = "2023-06-01",
    };

    [Fact]
    public async Task JudgmentTemplateChecklistExposesTaxesOwedAndNoTaxesOwedSections()
    {
        var checklist = await _fixture.Repository.GetDocumentGenerationChecklistAsync(1, "judgment_platform");

        Assert.NotNull(checklist);
        Assert.Contains(checklist!.Sections, s => s.SectionKey == "NoTaxesOwed");
        Assert.Contains(checklist.Sections, s => s.SectionKey == "TaxesOwed");
        var noTaxes = checklist.Sections.Single(s => s.SectionKey == "NoTaxesOwed");
        Assert.Contains(noTaxes.OverlapWarnings, w => w.Contains("Taxes Owed"));
    }

    [Fact]
    public async Task GeneratingJudgmentWithNoTaxesOwedProducesTheNoTaxesLanguageOnly()
    {
        var caseRecord = await CreateJudgmentCaseAsync();

        var result = await _fixture.Repository.GenerateDocumentPlatformDocumentAsync(
            caseRecord.Id, "judgment_platform", ["NoTaxesOwed"], JudgmentInputs(), null);

        Assert.Empty(result.MissingFields);
        using var doc = WordprocessingDocument.Open(result.OutputPath, false);
        var text = doc.MainDocumentPart!.Document.Body!.InnerText;
        Assert.Contains("There are no taxes due", text);
        Assert.DoesNotContain("current taxes due in the amount of", text);
        Assert.Contains("Smith Family Trust", text);
        Assert.DoesNotContain("{{", text);
    }

    [Fact]
    public async Task GeneratingJudgmentWithTaxesOwedProducesTheTaxAmountLanguageOnly()
    {
        var caseRecord = await CreateJudgmentCaseAsync(taxOwedAmount: 342.50m);

        var result = await _fixture.Repository.GenerateDocumentPlatformDocumentAsync(
            caseRecord.Id, "judgment_platform", ["TaxesOwed"], JudgmentInputs(), null);

        Assert.Empty(result.MissingFields);
        using var doc = WordprocessingDocument.Open(result.OutputPath, false);
        var text = doc.MainDocumentPart!.Document.Body!.InnerText;
        Assert.Contains("current taxes due in the amount of $342.50", text);
        Assert.DoesNotContain("There are no taxes due", text);
        Assert.DoesNotContain("{{", text);
    }

    [Fact]
    public async Task GeneratingSettlementJustificationFillsEveryManualField()
    {
        var caseRecord = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Settlement Case", CaseNumber = "23CV-502", County = "Pulaski", Status = "Active",
            Track = "Settlement", Tract = "3", JobNumber = "090111", ProjectName = "Hwy 10 Widening",
            Landowner = "Doe Family", WholePropertyAcres = 10m, AcquisitionAcres = 2m, FilingDate = "2023-01-05",
        });
        var org = await _fixture.Repository.GetOrgDefaultsAsync();
        org.AttorneyName = "Jane Doe";
        org.ChiefLegalCounselName = "Pat Counsel";
        org.DivisionHeadName = "Alex Division";
        org.RowSectionHeadName = "Sam Section";
        await _fixture.Repository.SaveOrgDefaultsAsync(org);

        var inputs = new Dictionary<string, string>
        {
            ["MemoDate"] = "2023-05-01",
            ["PropertyDescription"] = "a residential lot",
            ["HighestAndBestUse"] = "residential",
            ["TCEDescription"] = "None.",
            ["OurAppraisalTotal"] = "50000",
            ["OurAppraisalLandBefore"] = "40000",
            ["OurAppraisalPerSfBefore"] = "1.50",
            ["OurAppraisalLandAfter"] = "35000",
            ["OurAppraisalPerSfAfter"] = "1.25",
            ["DefendantAppraisalTotal"] = "70000",
            ["DefendantAppraisalAboveDeposit"] = "20000",
            ["DefendantAppraisalLandBefore"] = "55000",
            ["DefendantAppraisalPerSfBefore"] = "2.00",
            ["DefendantAppraisalLandAfter"] = "50000",
            ["DefendantAppraisalPerSfAfter"] = "1.90",
            ["ASHCOfferAmount"] = "55000",
            ["ASHCOfferDate"] = "2023-05-10",
            ["FeeAdjustmentAmount"] = "2000",
            ["CounterofferAmount"] = "65000",
            ["CounterofferDate"] = "2023-05-20",
            ["SettlementAmount"] = "60000",
            ["TrialFeeLow"] = "10000",
            ["TrialFeeHigh"] = "25000",
        };

        var result = await _fixture.Repository.GenerateDocumentPlatformDocumentAsync(
            caseRecord.Id, "settlement_justification_platform", [], inputs, null);

        Assert.Empty(result.MissingFields);
        using var doc = WordprocessingDocument.Open(result.OutputPath, false);
        var text = doc.MainDocumentPart!.Document.Body!.InnerText;
        Assert.Contains("Alex Division", text);
        Assert.Contains("Pat Counsel", text);
        Assert.Contains("Hwy 10 Widening", text);
        Assert.Contains("$60000", text);
        Assert.DoesNotContain("{{", text);
    }

    [Fact]
    public async Task GeneratingRequestsForAdmissionListsAllTwentySixRequestsWithJustCompensation()
    {
        var caseRecord = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "RFA Case", CaseNumber = "23CV-503", County = "Pulaski", Status = "Active",
            Track = "Contested", Landowner = "Smith Family Trust", JobNumber = "090554", ProjectName = "Hwy 10 Widening",
        });
        var org = await _fixture.Repository.GetOrgDefaultsAsync();
        org.AttorneyName = "Jane Doe";
        org.BarNumber = "2020123";
        org.Phone = "501-555-0100";
        org.Email = "jane.doe@ardot.gov";
        org.AddressLine1 = "10324 Interstate 30";
        org.AddressLine2 = "Little Rock, AR 72209";
        await _fixture.Repository.SaveOrgDefaultsAsync(org);

        var inputs = new Dictionary<string, string> { ["JustCompensationAmount"] = "15000", ["CertificateDate"] = "2023-07-01" };

        var result = await _fixture.Repository.GenerateDocumentPlatformDocumentAsync(
            caseRecord.Id, "requests_for_admission_platform", [], inputs, null);

        Assert.Empty(result.MissingFields);
        using var doc = WordprocessingDocument.Open(result.OutputPath, false);
        var text = doc.MainDocumentPart!.Document.Body!.InnerText;
        Assert.Contains("REQUEST FOR ADMISSION NO. 1:", text);
        Assert.Contains("REQUEST FOR ADMISSION NO. 26:", text);
        Assert.Contains("$15000", text);
        Assert.Contains("CERTIFICATE OF SERVICE", text);
        Assert.DoesNotContain("{{", text);
    }

    [Fact]
    public async Task AllThreeNewBuiltinsAppearInTheAdminTemplateList()
    {
        var all = await _fixture.Repository.GetAllDocumentTemplatesForAdminAsync();

        Assert.Contains(all, t => t.Template.TemplateKey == "judgment_platform" && t.Template.IsBuiltin);
        Assert.Contains(all, t => t.Template.TemplateKey == "settlement_justification_platform" && t.Template.IsBuiltin);
        Assert.Contains(all, t => t.Template.TemplateKey == "requests_for_admission_platform" && t.Template.IsBuiltin);
    }
}
