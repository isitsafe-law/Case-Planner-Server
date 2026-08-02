using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Persistence;

namespace CasePlanner.Web.Server.Services;

/// <summary>
/// Provider-neutral report query used by server-generated exports. The first migrated report is the
/// case-list report; other report tabs continue to use the validated client result until their
/// aggregate query contracts are moved here.
/// </summary>
public sealed class ReportExportQueryService(ICaseCatalogReader cases)
{
    private static readonly HashSet<string> OpenStatuses = ["Pipeline", "Filed / Service Pending", "Active Litigation", "Settlement Pending", "Trial Preparation"];

    public async Task<List<Dictionary<string, string>>> GetCaseListRowsAsync(ReportExcelRequest request, IReadOnlySet<long>? visibleCaseIds, CancellationToken token)
    {
        var records = await cases.GetCasesAsync(new CaseCatalogQuery(IncludeClosed: true), token);
        if (visibleCaseIds is not null) records = records.Where(record => visibleCaseIds.Contains(record.Id)).ToList();

        var status = request.Filters.GetValueOrDefault("status", "all");
        var county = request.Filters.GetValueOrDefault("county", "all");
        var district = request.Filters.GetValueOrDefault("district", "all");
        var search = request.Filters.GetValueOrDefault("search", "all").Trim();
        var from = request.Filters.GetValueOrDefault("dateOpenedFrom", "");
        var to = request.Filters.GetValueOrDefault("dateOpenedTo", "");
        var includeClosed = status.Equals("__closed", StringComparison.OrdinalIgnoreCase);

        records = records.Where(record =>
        {
            var caseStatus = string.IsNullOrWhiteSpace(record.CaseStatus) ? "Pipeline" : record.CaseStatus;
            if (!includeClosed && !OpenStatuses.Contains(caseStatus)) return false;
            if (!includeClosed && record.Status.Equals("Triage", StringComparison.OrdinalIgnoreCase)) return false;
            if (includeClosed && OpenStatuses.Contains(caseStatus)) return false;
            if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase) && !status.Equals("__closed", StringComparison.OrdinalIgnoreCase) && !caseStatus.Equals(status, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrWhiteSpace(county) && !county.Equals("all", StringComparison.OrdinalIgnoreCase) && !record.County.Equals(county, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrWhiteSpace(district) && !district.Equals("all", StringComparison.OrdinalIgnoreCase) && !string.Equals(record.District, district, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrWhiteSpace(from) && (string.IsNullOrWhiteSpace(record.DateOpened) || string.CompareOrdinal(record.DateOpened, from) < 0)) return false;
            if (!string.IsNullOrWhiteSpace(to) && (string.IsNullOrWhiteSpace(record.DateOpened) || string.CompareOrdinal(record.DateOpened, to) > 0)) return false;
            if (!string.IsNullOrWhiteSpace(search) && !string.Join(" ", record.CaseName, record.CaseNumber, record.JobNumber, record.Tract, record.County, record.ProjectName).Contains(search, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }).ToList();

        var sortColumn = request.Filters.GetValueOrDefault("sortColumn", "dateOpened");
        var descending = request.Filters.GetValueOrDefault("sortDirection", "asc").Equals("desc", StringComparison.OrdinalIgnoreCase);
        records = (descending ? records.OrderByDescending(record => Value(record, sortColumn)) : records.OrderBy(record => Value(record, sortColumn))).ThenBy(record => record.Id).ToList();
        return records.Select(record => request.Columns.ToDictionary(column => column.Key, column => Value(record, column.Key))).ToList();
    }

    private static string Value(CaseRecord record, string key) => key switch
    {
        "caseName" => record.CaseName,
        "caseNumber" => record.CaseNumber,
        "county" => record.County,
        "district" => record.District ?? "",
        "jobNumber" => record.JobNumber,
        "tract" => record.Tract,
        "projectName" => record.ProjectName ?? "",
        "caseStatus" => record.CaseStatus,
        "currentHolder" => record.CurrentHolder ?? "",
        "nextAction" => record.NextAction ?? "",
        "nextReviewDate" => record.NextReviewDate ?? "",
        "trialDate" => record.TrialDate ?? "",
        "takingType" => record.TakingType ?? "",
        "dispositionType" => record.DispositionType ?? "",
        "finalJudgmentAmount" => record.FinalJudgmentAmount?.ToString() ?? "",
        "dateOpened" => record.DateOpened ?? "",
        "closedDate" => record.ClosedDate ?? "",
        "caseAgeDays" => AgeDays(record.DateOpened, record.ClosedDate),
        _ => ""
    };

    private static string AgeDays(string? opened, string? closed)
    {
        if (!DateTime.TryParse(opened, out var start)) return "";
        var end = DateTime.TryParse(closed, out var parsedClosed) ? parsedClosed : DateTime.Today;
        var days = (end.Date - start.Date).Days;
        return days < 0 ? "" : days.ToString();
    }
}
