using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Services;

// Ports the formulas from the office's real "Risk Analysis" workbook: each offer/position
// row gets a 6% simple-interest accrual and the Ark. Code Ann. § 27-67-317(b) 20%-over-deposit
// fee-shift test. Pure computation, no DB access — callers assemble the case + row input and
// pass them in. Every value is recomputed live (including interest, which is a function of
// "today"), matching the original spreadsheet's live-formula behavior rather than a frozen
// snapshot. Rows are the fixed 5-row structure the real workbook uses (see RiskAnalysisRowInput)
// — this same computation is re-emitted as literal Excel formulas by
// RiskAnalysisExcelExportService for the .xlsx export, so any formula change here must be
// mirrored there.
public static class RiskAnalysisEngine
{
    public static RiskAnalysisResult Compute(CaseRecord c, RiskAnalysisInput input)
    {
        var initialDeposit = c.DepositAmount ?? 0m;
        var additionalDeposit = c.AdditionalDepositAmount ?? 0m;
        var totalDeposited = initialDeposit + additionalDeposit;
        var filingDate = ParseDate(c.FilingDate);
        var additionalDepositDate = ParseDate(c.AdditionalDepositDate);
        // Analysis date is captured with the snapshot so reopening an older analysis does
        // not silently change its interest calculation. A missing/invalid date falls back
        // to today for backward-compatible previews, while the UI can prompt for it.
        var today = ParseDate(input.AnalysisDate) ?? DateOnly.FromDateTime(DateTime.Today);
        var interestRate = input.InterestRate <= 0 ? 0.06m : input.InterestRate;
        var contingencyFeePercent = input.ContingencyFeePercent < 0 ? 0.30m : input.ContingencyFeePercent;
        int? daysSinceFiling = filingDate is { } fd ? today.DayNumber - fd.DayNumber : null;

        var rows = new List<RiskAnalysisRowResult>();
        foreach (var rowInput in input.Rows)
        {
            var row = ComputeRow(rowInput.RowKey, rowInput.Label, rowInput.OfferMaker, false,
                rowInput.JustCompensation, rowInput.LandownerFeesCosts, rowInput.AshcCosts, rowInput.HourlyFeesRisk,
                initialDeposit, totalDeposited, filingDate, additionalDeposit, additionalDepositDate, today, interestRate, contingencyFeePercent);
            rows.Add(row);

            if (rowInput.IncludeSplit)
            {
                var splitJustCompensation = rowInput.JustCompensation is { } jc ? (totalDeposited + jc) / 2m : (decimal?)null;
                var splitRow = ComputeRow(rowInput.RowKey + "Split", rowInput.Label + " - Split", rowInput.OfferMaker, true,
                    splitJustCompensation, rowInput.LandownerFeesCosts, rowInput.AshcCosts, rowInput.HourlyFeesRisk,
                    initialDeposit, totalDeposited, filingDate, additionalDeposit, additionalDepositDate, today, interestRate, contingencyFeePercent);
                rows.Add(splitRow);
            }
        }

        return new RiskAnalysisResult
        {
            CaseId = input.CaseId,
            Narrative = input.Narrative,
            InitialDeposit = initialDeposit,
            AdditionalDeposit = additionalDeposit,
            TotalDeposited = totalDeposited,
            DaysSinceFiling = daysSinceFiling,
            AnalysisDate = today.ToString("yyyy-MM-dd"),
            InterestRate = interestRate,
            ContingencyFeePercent = contingencyFeePercent,
            Rows = rows
        };
    }

    private static RiskAnalysisRowResult ComputeRow(
        string key, string label, string offerMaker, bool isSplit, decimal? justCompensation,
        decimal landownerFeesCosts, decimal ashcCosts, decimal hourlyFeesRisk,
        decimal initialDeposit, decimal totalDeposited, DateOnly? filingDate,
        decimal additionalDeposit, DateOnly? additionalDepositDate, DateOnly today,
        decimal interestRate, decimal contingencyFeePercent)
    {
        var result = new RiskAnalysisRowResult
        {
            RowKey = key,
            Label = label,
            OfferMaker = offerMaker,
            IsSplit = isSplit,
            JustCompensation = justCompensation,
            LandownerFeesCosts = landownerFeesCosts,
            AshcCosts = ashcCosts,
            HourlyFeesRisk = hourlyFeesRisk
        };

        if (justCompensation is not { } jc || jc == 0)
        {
            result.AmountAboveInitialDeposit = 0;
            result.InterestOnOverage = 0;
            result.Subtotal = 0;
            result.ContingencyFee = 0;
            result.TotalRiskHourly = 0;
            result.HourlyRiskStatus = "NotApplicable";
            result.TotalRiskContingency = 0;
            return result;
        }

        result.AmountAboveInitialDeposit = jc - initialDeposit;

        if (filingDate is null)
        {
            result.Note = "Add a filing date to this case to compute accrued interest.";
            return result;
        }

        var hasAdditionalDeposit = additionalDeposit != 0 && additionalDepositDate is not null;
        var cutoff1 = hasAdditionalDeposit ? Min(additionalDepositDate!.Value, today) : today;
        var daysPeriod1 = Math.Max(cutoff1.DayNumber - filingDate.Value.DayNumber, 0);
        var term1 = Math.Max(jc - initialDeposit, 0m) * interestRate * daysPeriod1 / 365m;

        var term2 = 0m;
        if (hasAdditionalDeposit)
        {
            var daysPeriod2 = Math.Max(today.DayNumber - additionalDepositDate!.Value.DayNumber, 0);
            term2 = Math.Max(jc - initialDeposit - additionalDeposit, 0m) * interestRate * daysPeriod2 / 365m;
        }

        var interest = term1 + term2;
        result.InterestOnOverage = interest;
        var subtotal = jc + interest;
        result.Subtotal = subtotal;
        result.ContingencyFee = subtotal * contingencyFeePercent;

        // Ark. Code Ann. § 27-67-317(b): fee-shift risk applies only when a jury verdict
        // (or, here, the scenario amount) exceeds the total deposited by 20% or more.
        // If nothing has been deposited yet, treat any positive amount as exceeding the
        // threshold rather than dividing by zero.
        var overThreshold = totalDeposited == 0 ? jc > 0 : (jc - totalDeposited) / totalDeposited >= 0.2m;
        if (overThreshold)
        {
            result.TotalRiskHourly = subtotal + landownerFeesCosts + ashcCosts + hourlyFeesRisk;
            result.HourlyRiskStatus = "Computed";
        }
        else
        {
            result.TotalRiskHourly = null;
            result.HourlyRiskStatus = "BelowThreshold";
        }

        result.TotalRiskContingency = subtotal + ashcCosts + result.ContingencyFee;
        return result;
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static DateOnly? ParseDate(string? value) => DateOnly.TryParse(value, out var d) ? d : null;
}
