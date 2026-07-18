using System.Globalization;
using System.Text.Json;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public sealed record RiskAnalysisMismatch(string Kind, long Id, string Field, string? SqliteValue, string? SqlServerValue);
public sealed record RiskAnalysisReconciliation(
    bool Matches,
    long CaseId,
    int SqliteHistoryCount,
    int SqlServerHistoryCount,
    int SqliteOfferCount,
    int SqlServerOfferCount,
    List<RiskAnalysisMismatch> Mismatches);

public sealed class RiskAnalysisReconciliationService(
    CasePlannerRepository sqlite,
    SqlServerRiskAnalysisStore sqlRisk,
    SqlServerRiskOfferStore sqlOffers)
{
    public async Task<RiskAnalysisReconciliation> CompareAsync(long caseId, CancellationToken token = default)
    {
        var left = await sqlite.GetRiskAnalysisAsync(caseId);
        var right = await sqlRisk.GetAsync(caseId, token);
        var leftHistory = await sqlite.GetRiskAnalysisHistoryAsync(caseId);
        var rightHistory = await sqlRisk.GetHistoryAsync(caseId, token);
        var leftOffers = await sqlite.GetOfferLogAsync(caseId);
        var rightOffers = await sqlOffers.GetAsync(caseId, token);
        var mismatches = new List<RiskAnalysisMismatch>();

        Compare("Current", left.Id, "Narrative", left.Narrative, right.Narrative, mismatches);
        Compare("Current", left.Id, "AnalysisDate", left.AnalysisDate, right.AnalysisDate, mismatches);
        Compare("Current", left.Id, "InterestRate", Value(left.InterestRate), Value(right.InterestRate), mismatches);
        Compare("Current", left.Id, "ContingencyFeePercent", Value(left.ContingencyFeePercent), Value(right.ContingencyFeePercent), mismatches);
        Compare("Current", left.Id, "Rows", JsonSerializer.Serialize(left.Rows), JsonSerializer.Serialize(right.Rows), mismatches);

        CompareSets(leftHistory.ToDictionary(x => x.Id), rightHistory.ToDictionary(x => x.Id), "History", mismatches,
            (a, b, id) =>
            {
                Compare("History", id, "AnalysisDate", a.AnalysisDate, b.AnalysisDate, mismatches);
                Compare("History", id, "FormulaVersion", a.FormulaVersion, b.FormulaVersion, mismatches);
                Compare("History", id, "Narrative", a.Narrative, b.Narrative, mismatches);
                Compare("History", id, "Rows", JsonSerializer.Serialize(a.Rows), JsonSerializer.Serialize(b.Rows), mismatches);
                Compare("History", id, "InterestRate", Value(a.InterestRate), Value(b.InterestRate), mismatches);
                Compare("History", id, "ContingencyFeePercent", Value(a.ContingencyFeePercent), Value(b.ContingencyFeePercent), mismatches);
                Compare("History", id, "KeyScenarioLabel", a.KeyScenarioLabel, b.KeyScenarioLabel, mismatches);
                Compare("History", id, "KeyScenarioValue", Value(a.KeyScenarioValue), Value(b.KeyScenarioValue), mismatches);
                Compare("History", id, "KeyScenarioOrder", Value(a.KeyScenarioOrder), Value(b.KeyScenarioOrder), mismatches);
            });
        CompareSets(leftOffers.ToDictionary(x => x.Id), rightOffers.ToDictionary(x => x.Id), "Offer", mismatches,
            (a, b, id) =>
            {
                Compare("Offer", id, "CaseId", Value(a.CaseId), Value(b.CaseId), mismatches);
                Compare("Offer", id, "OfferDate", a.OfferDate, b.OfferDate, mismatches);
                Compare("Offer", id, "Party", a.Party, b.Party, mismatches);
                Compare("Offer", id, "Amount", Value(a.Amount), Value(b.Amount), mismatches);
            });

        return new(mismatches.Count == 0, caseId, leftHistory.Count, rightHistory.Count, leftOffers.Count, rightOffers.Count, mismatches.Take(100).ToList());
    }

    private static void CompareSets<T>(
        Dictionary<long, T> left,
        Dictionary<long, T> right,
        string kind,
        List<RiskAnalysisMismatch> result,
        Action<T, T, long> compare)
    {
        foreach (var id in left.Keys.Except(right.Keys)) result.Add(new(kind, id, "Record", "Present", "Missing"));
        foreach (var id in right.Keys.Except(left.Keys)) result.Add(new(kind, id, "Record", "Missing", "Present"));
        foreach (var id in left.Keys.Intersect(right.Keys)) compare(left[id], right[id], id);
    }

    private static string? Value(object? value) => value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    private static void Compare(string kind, long id, string field, string? left, string? right, List<RiskAnalysisMismatch> result)
    {
        if (!string.Equals(left, right, StringComparison.Ordinal)) result.Add(new(kind, id, field, left, right));
    }
}
