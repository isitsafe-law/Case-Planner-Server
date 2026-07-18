using System.Globalization;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

// Build-plan step 7 (cleanup): this used to also compose the 5 old built-in document kinds and
// old custom-template previews/generation - all retired in favor of the unified document platform
// (DocumentPlatformService). Risk Analysis narrative generation is a separate, still-live feature
// that happened to share this provider-neutral SQLite/SQL-Server composition boundary, so it's
// the only thing left here rather than being folded away with the rest.
public interface IDocumentCompositionService
{
    Task<string> GenerateRiskNarrativeAsync(long caseId, RiskNarrativeManualInputs manual, CancellationToken token = default);
}

public sealed class SqliteDocumentCompositionService(CasePlannerRepository repository) : IDocumentCompositionService
{
    public Task<string> GenerateRiskNarrativeAsync(long caseId, RiskNarrativeManualInputs manual, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GenerateRiskNarrativeAsync(caseId, manual);
    }
}

public sealed class SqlServerDocumentCompositionService(
    SqlServerWorkspaceQuery workspaceQuery,
    SqlServerValuationPositionStore valuationPositions,
    SqlServerRiskAnalysisStore riskAnalysis) : IDocumentCompositionService
{
    public async Task<string> GenerateRiskNarrativeAsync(long caseId, RiskNarrativeManualInputs manual, CancellationToken token = default)
    {
        var workspace = await WorkspaceAsync(caseId, token);
        var positions = await valuationPositions.GetAsync(caseId, token);
        var risk = await riskAnalysis.GetAsync(caseId, token);
        return DocumentCompositionRules.BuildRiskNarrative(
            workspace.Case,
            positions.FirstOrDefault(position => position.Side == "ASHC"),
            positions.FirstOrDefault(position => position.Side == "Landowner"),
            risk,
            manual);
    }

    private async Task<CaseWorkspaceResponse> WorkspaceAsync(long caseId, CancellationToken token) =>
        await workspaceQuery.GetWorkspaceAsync(caseId, null, token) ?? throw new InvalidOperationException("Case not found.");
}

public static class DocumentCompositionRules
{
    public static string BuildRiskNarrative(
        CaseRecord caseRecord,
        ValuationPositionRecord? ashcPosition,
        ValuationPositionRecord? landownerPosition,
        RiskAnalysisResult risk,
        RiskNarrativeManualInputs manual)
    {
        static string Money(decimal? value) => value.HasValue ? value.Value.ToString("C", CultureInfo.CurrentCulture) : "Not set";
        static string PerSf(decimal? value) => value.HasValue ? value.Value.ToString("C", CultureInfo.CurrentCulture) + "/sf" : "Not set/sf";
        static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "Not set" : value;
        static string Date(string? value) => DateOnly.TryParse(value, out var parsed) ? parsed.ToString("yyyy-MM-dd") : "Not set";

        var ashcOffer = risk.Rows.FirstOrDefault(row => !row.IsSplit && row.OfferMaker == "ASHC");
        var counteroffer = risk.Rows.LastOrDefault(row => !row.IsSplit && row.OfferMaker == "Landowner");
        var defendantAboveDeposit = landownerPosition?.AppraisedValue is { } defendantTotal
            ? defendantTotal - (caseRecord.DepositAmount ?? 0m)
            : (decimal?)null;
        var tceSentence = string.IsNullOrWhiteSpace(manual.TceDescription) ? "" : $" {manual.TceDescription}";
        var paragraph1 =
            $"This condemnation was filed on {Date(caseRecord.FilingDate)}, for the purposes of constructing and maintaining highway facilities on {Text(caseRecord.ProjectName)}, " +
            $"in connection with Job No. {Text(caseRecord.JobNumber)}, which involved the acquisition of Tract {Text(caseRecord.Tract)}. " +
            $"The whole existing property was {caseRecord.WholePropertyAcres?.ToString() ?? "Not set"} acres, more or less. The property is {Text(manual.PropertyDescription)}. " +
            $"A total of {caseRecord.AcquisitionAcres?.ToString() ?? "Not set"} acres, more or less, was acquired by the ASHC.{tceSentence}";
        var paragraph2 =
            $"ARDOT prepared a before-and-after appraisal that valued the total acquisition at {Money(ashcPosition?.AppraisedValue)}. " +
            $"The land was valued at {Money(manual.OurAppraisalLandBefore)} ({PerSf(manual.OurAppraisalPerSfBefore)}) before the acquisition and {Money(manual.OurAppraisalLandAfter)} ({PerSf(manual.OurAppraisalPerSfAfter)}) after the acquisition, based on comparable sales. " +
            $"Defendants obtained an appraisal for the amount of {Money(landownerPosition?.AppraisedValue)}, an additional {Money(defendantAboveDeposit)} above the initial deposit. " +
            "Defendants' appraisal also valued the land much higher than the value given by the ARDOT appraisal. " +
            $"The property was valued at {Money(manual.DefendantAppraisalLandBefore)} ({PerSf(manual.DefendantAppraisalPerSfBefore)}) before the acquisition and {Money(manual.DefendantAppraisalLandAfter)} ({PerSf(manual.DefendantAppraisalPerSfAfter)}) after the acquisition, based on comparable sales. " +
            $"Both appraisals found that the property had a highest and best use of {Text(manual.HighestAndBestUse)}.";
        var thresholdSentence = ashcOffer?.HourlyRiskStatus == "Computed"
            ? $"As the total amount of {Money(ashcOffer?.JustCompensation)} equals at least 20% above the initial deposit, it would trigger the automatic statutory award of attorney's fees under Ark. Code Ann. § 27-67-317(b) if such an award were made at trial."
            : $"As the total amount of {Money(ashcOffer?.JustCompensation)} does not equal at least 20% above the initial deposit, it would not trigger the automatic statutory award of attorney's fees if such an award were made at trial.";
        var paragraph3 =
            $"Based on the valuation proffered by Defendants' appraisal, a proposal by ASHC of {Money(ashcOffer?.JustCompensation)} was made on {Text(manual.AshcOfferDate)}. " +
            $"An adjustment of {Money(manual.FeeAdjustmentAmount)} was made on the initial offer to account for expenses and attorney's fees. " +
            thresholdSentence;
        var paragraph4 =
            $"On {Text(manual.CounterofferDate)}, Defendants made a counteroffer in the amount of {Money(counteroffer?.JustCompensation)}. " +
            $"Settlement for the sum of {Money(manual.SettlementAmount)} is reasonable. " +
            $"Potential risk of taking the matter to trial could result in total fees of {Money(manual.TrialFeeLow)}-{Money(manual.TrialFeeHigh)} on a {Money(manual.SettlementAmount)} judgment.";
        return string.Join(Environment.NewLine + Environment.NewLine,
            paragraph1, paragraph2, "[Add any other necessary specifics about the property and/or negotiations.]", paragraph3, paragraph4);
    }
}
