using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Tests;

public sealed class MergeTagRegistryTests
{
    [Fact]
    public void EveryRegisteredTagHasAResolverEntryAndPopulatedFixtureValue()
    {
        var caseRecord = new CaseRecord
        {
            CaseNumber = "60CV-26-1234", CaseName = "Test Case", County = "Pulaski", JobNumber = "JOB-1", Tract = "001",
            ProjectName = "Test Highway", CaseStatus = "Active Litigation", CaseStyle = "Arkansas State Highway Commission v. Smith",
            Landowner = "John Smith", FilingDate = "2026-01-02", DateOpened = "2026-01-01", DateOfTaking = "2026-01-03",
            ClosedDate = "2026-06-01", TrialDate = "2026-07-29", TrialEndDate = "2026-07-31", NextAction = "Prepare exhibit list",
            NextActionDue = "2026-07-10", DepositAmount = 1000, WholePropertyAcres = 10, AcquisitionAcres = 2, TaxOwedAmount = 25,
            FapNumber = "FAP-1", ParcelNumber = "P-1", Judge = "Judge Test", Division = "Division 1", AssignedAttorney = "A. Attorney",
            OpposingCounsel = "C. Counsel", OpposingCounselContact = "counsel@example.com", ServiceStatus = "Perfected", ServicePerfectedDate = "2026-02-01"
        };
        var org = new OrgDefaults
        {
            AttorneyName = "A. Attorney", BarNumber = "12345", Phone = "555-0100", Email = "attorney@example.com",
            AddressLine1 = "1 Main Street", AddressLine2 = "Little Rock, AR 72201", DivisionHeadName = "Division Head",
            RowSectionHeadName = "ROW Head", ChiefLegalCounselName = "Chief Counsel"
        };

        var tokens = DocumentGenerationEngine.BuildTokens(caseRecord, org, new Dictionary<string, string>());
        var tags = DocumentGenerationEngine.GetAllTemplateTags();

        Assert.NotEmpty(tags);
        Assert.All(tags, tag => Assert.True(tokens.ContainsKey(tag.Key), $"No resolver entry for {tag.Key}"));
        Assert.All(tags, tag => Assert.False(string.IsNullOrWhiteSpace(tokens[tag.Key]), $"Fixture did not populate {tag.Key}"));
    }

    [Fact]
    public void MissingRegisteredValueDoesNotBlockGeneration()
    {
        var tokens = DocumentGenerationEngine.BuildTokens(new CaseRecord(), new OrgDefaults(), new Dictionary<string, string>());

        var result = DocumentGenerationEngine.FillTemplate("{{Case.FullStyle}} {{Case.TrialDate}}", tokens, out var missing);

        Assert.Contains("[MISSING: Case.FullStyle]", result);
        Assert.Contains("Case.TrialDate", missing);
    }

    [Fact]
    public void AuditSeparatesUnknownTagsFromKnownBlankValuesAndRuntimeInputs()
    {
        var audit = DocumentGenerationEngine.AuditTemplateTags(
            ["CaseNumber", "Case.TrialDate", "Runtime.Deposit", "Legacy.Leftover"],
            new Dictionary<string, string>
            {
                ["CaseNumber"] = "60CV-26-1234",
                ["Case.TrialDate"] = "",
                ["Runtime.Deposit"] = "1000",
            },
            ["Runtime.Deposit"]);

        Assert.Equal(new[] { "Case.TrialDate", "CaseNumber", "Legacy.Leftover", "Runtime.Deposit" }.OrderBy(x => x), audit.DiscoveredTags);
        Assert.Equal(new[] { "Case.TrialDate", "CaseNumber", "Runtime.Deposit" }.OrderBy(x => x), audit.KnownTags);
        Assert.Equal(["Legacy.Leftover"], audit.UnknownTags);
        Assert.Equal(["Case.TrialDate"], audit.BlankValues);
    }

    [Fact]
    public void TemplateTagsAreResolvedWithoutCaseSensitiveFalseMissingValues()
    {
        var tokens = DocumentGenerationEngine.BuildTokens(new CaseRecord { County = "Pulaski" }, new OrgDefaults(), new Dictionary<string, string>());

        var result = DocumentGenerationEngine.FillTemplate("{{COUNTY}}", tokens, out var missing);

        Assert.Equal("Pulaski", result);
        Assert.Empty(missing);
    }
}
