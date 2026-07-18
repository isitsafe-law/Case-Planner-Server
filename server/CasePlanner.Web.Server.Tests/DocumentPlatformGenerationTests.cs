using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// Build-plan step 4 (unified case UI): the real pipeline - case + seed template + case's actual
// issue tags -> checklist -> generation -> a real merged .docx recorded in document_generations.
public sealed class DocumentPlatformGenerationTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<CaseRecord> CreateCaseWithDrainageTagAsync()
    {
        var caseRecord = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Test Case",
            CaseNumber = "23CV-999",
            County = "Pulaski",
            Landowner = "Smith Family Trust",
            Status = "Active",
            CaseStatus = "Active Litigation",
            Track = "Contested",
        });

        var drainageTag = (await _fixture.Repository.GetIssueTagsAsync()).Single(t => t.Name == "Drainage");
        await _fixture.Repository.AddIssueTagAsync(caseRecord.Id, drainageTag.Id);

        var org = await _fixture.Repository.GetOrgDefaultsAsync();
        org.AttorneyName = "Jane Doe";
        await _fixture.Repository.SaveOrgDefaultsAsync(org);

        return caseRecord;
    }

    [Fact]
    public async Task SeedTemplateExistsAfterInitialization()
    {
        var checklist = await _fixture.Repository.GetDocumentGenerationChecklistAsync(1, "interrogatories_platform");
        Assert.NotNull(checklist);
        Assert.Equal("interrogatories_platform", checklist!.TemplateKey);
        Assert.Contains(checklist.Sections, s => s.SectionKey == "Drainage");
    }

    [Fact]
    public async Task UnknownTemplateKeyReturnsNullChecklist()
    {
        var checklist = await _fixture.Repository.GetDocumentGenerationChecklistAsync(1, "does-not-exist");
        Assert.Null(checklist);
    }

    [Fact]
    public async Task DrainageSectionIsPreCheckedWhenCaseHasTheDrainageTag()
    {
        var caseRecord = await CreateCaseWithDrainageTagAsync();

        var checklist = await _fixture.Repository.GetDocumentGenerationChecklistAsync(caseRecord.Id, "interrogatories_platform");

        var drainage = checklist!.Sections.Single(s => s.SectionKey == "Drainage");
        Assert.True(drainage.IsDefaultChecked);
    }

    [Fact]
    public async Task DrainageSectionIsNotPreCheckedWithoutTheTag()
    {
        var caseRecord = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "No Tag Case", CaseNumber = "23CV-998", County = "Pulaski", Status = "Active", Track = "Contested",
        });

        var checklist = await _fixture.Repository.GetDocumentGenerationChecklistAsync(caseRecord.Id, "interrogatories_platform");

        var drainage = checklist!.Sections.Single(s => s.SectionKey == "Drainage");
        Assert.False(drainage.IsDefaultChecked);
    }

    [Fact]
    public async Task GeneratingWithDrainageSelectedProducesARealDocxWithTheDrainageQuestion()
    {
        var caseRecord = await CreateCaseWithDrainageTagAsync();

        var result = await _fixture.Repository.GenerateDocumentPlatformDocumentAsync(
            caseRecord.Id, "interrogatories_platform", ["Drainage"], new Dictionary<string, string>(), null);

        Assert.True(result.GenerationId > 0);
        Assert.Empty(result.MissingFields);
        Assert.True(File.Exists(result.OutputPath));

        var record = await _fixture.Repository.GetDocumentGenerationByIdAsync(result.GenerationId);
        Assert.NotNull(record);
        Assert.Equal(caseRecord.Id, record!.CaseId);
        Assert.Contains("Drainage", record.SectionsIncluded);

        using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(result.OutputPath, false);
        var text = doc.MainDocumentPart!.Document.Body!.InnerText;
        Assert.Contains("PULASKI COUNTY, ARKANSAS", text);
        Assert.Contains("Smith Family Trust", text);
        Assert.Contains("Describe any change in surface water drainage", text);
        Assert.DoesNotContain("{{", text);
    }

    [Fact]
    public async Task GeneratingWithoutDrainageSelectedOmitsTheDrainageQuestion()
    {
        var caseRecord = await CreateCaseWithDrainageTagAsync();

        var result = await _fixture.Repository.GenerateDocumentPlatformDocumentAsync(
            caseRecord.Id, "interrogatories_platform", [], new Dictionary<string, string>(), null);

        using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(result.OutputPath, false);
        var text = doc.MainDocumentPart!.Document.Body!.InnerText;
        Assert.DoesNotContain("Describe any change in surface water drainage", text);
        Assert.DoesNotContain("{{", text);
    }

    [Fact]
    public async Task GenerationIsRecordedInHistoryEvenAcrossMultipleGenerationsForTheSameCase()
    {
        var caseRecord = await CreateCaseWithDrainageTagAsync();

        var first = await _fixture.Repository.GenerateDocumentPlatformDocumentAsync(
            caseRecord.Id, "interrogatories_platform", ["Drainage"], new Dictionary<string, string>(), null);
        var second = await _fixture.Repository.GenerateDocumentPlatformDocumentAsync(
            caseRecord.Id, "interrogatories_platform", [], new Dictionary<string, string>(), null);

        Assert.NotEqual(first.GenerationId, second.GenerationId);
        Assert.NotEqual(first.OutputPath, second.OutputPath);
    }

    // Build-plan step 7 (cleanup): the retired IssueTagDiscoveryContent.cs held real drafted
    // interrogatory/RFP language for 14 issue tags beyond Drainage - ported into the platform
    // template as version 2 (EnsureInterrogatoriesAllIssueTagSectionsAsync) rather than lost when
    // the old class was deleted. These prove the port actually took and the content still merges.
    [Fact]
    public async Task InterrogatoriesTemplateUpgradedToVersionTwoWithAllFifteenTagSections()
    {
        var checklist = await _fixture.Repository.GetDocumentGenerationChecklistAsync(1, "interrogatories_platform");

        Assert.NotNull(checklist);
        Assert.Equal(2, checklist!.TemplateVersion);
        var expectedSections = new[]
        {
            "FullTaking", "EasementOnly", "TemporaryConstructionEasement", "SeveranceDamages",
            "AccessChangeOfAccess", "Drainage", "LandlockedRemainder", "Minerals", "Timber",
            "BillboardSign", "LeaseholdTenantInterest", "LienholderMortgage", "EstateProbate",
            "UnknownHeirsOwners", "UtilityConflict",
        };
        foreach (var key in expectedSections)
        {
            Assert.Contains(checklist.Sections, s => s.SectionKey == key);
        }
    }

    [Fact]
    public async Task GeneratingWithFullTakingAndTimberSectionsProducesTheirDraftedLanguage()
    {
        var caseRecord = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Tag Section Case", CaseNumber = "23CV-901", County = "Pulaski", Landowner = "Doe Trust",
            Status = "Active", Track = "Contested",
        });

        var result = await _fixture.Repository.GenerateDocumentPlatformDocumentAsync(
            caseRecord.Id, "interrogatories_platform", ["FullTaking", "Timber"], new Dictionary<string, string>(), null);

        using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(result.OutputPath, false);
        var text = doc.MainDocumentPart!.Document.Body!.InnerText;
        Assert.Contains("usable remainder after Plaintiff's acquisition", text);
        Assert.Contains("merchantable timber", text);
        Assert.DoesNotContain("Describe any change in surface water drainage", text);
        Assert.DoesNotContain("{{", text);
    }
}
