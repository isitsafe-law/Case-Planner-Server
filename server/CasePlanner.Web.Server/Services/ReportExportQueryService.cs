using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Persistence;

namespace CasePlanner.Web.Server.Services;

/// <summary>
/// Provider-neutral report query used by server-generated exports. The first migrated report is the
/// case-list report; other report tabs continue to use the validated client result until their
/// aggregate query contracts are moved here.
/// </summary>
public sealed class ReportExportQueryService(ICaseCatalogReader cases, IHearingStore hearings, ICaseAttorneyAssignmentStore assignments)
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

    public async Task<List<Dictionary<string, string>>> GetUpcomingTrialRowsAsync(ReportExcelRequest request, IReadOnlySet<long>? visibleCaseIds, CancellationToken token)
    {
        var horizonText = request.Filters.GetValueOrDefault("horizon", "all upcoming");
        var horizon = horizonText.StartsWith("next ", StringComparison.OrdinalIgnoreCase) && int.TryParse(horizonText.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1), out var parsed) ? parsed : (int?)null;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var latest = horizon.HasValue ? today.AddDays(horizon.Value) : DateOnly.MaxValue;
        var attorney = request.Filters.GetValueOrDefault("attorney", "all");
        var division = request.Filters.GetValueOrDefault("division", "all");
        var caseRows = await cases.GetCasesAsync(new CaseCatalogQuery(IncludeClosed: true), token);
        if (visibleCaseIds is not null) caseRows = caseRows.Where(record => visibleCaseIds.Contains(record.Id)).ToList();
        var caseById = caseRows.ToDictionary(record => record.Id);
        var assignmentRows = await assignments.GetAsync(null, token);
        var namesByCase = assignmentRows.GroupBy(row => row.CaseId).ToDictionary(group => group.Key, group => group.Select(row => row.Name).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        var eventRows = await hearings.GetAsync(null, token);
        var result = new List<(CaseRecord Case, HearingRecord Event, string Additional, int Days)>();
        foreach (var eventRow in eventRows.Where(row => row.EventType.Equals("Jury Trial", StringComparison.Ordinal) && !row.Status.Equals("Canceled", StringComparison.OrdinalIgnoreCase) && !row.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) && !row.Status.Equals("Complete", StringComparison.OrdinalIgnoreCase) && !row.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase)))
        {
            if (!caseById.TryGetValue(eventRow.CaseId, out var caseRow) || !DateOnly.TryParse(eventRow.HearingDate, out var start)) continue;
            var end = DateOnly.TryParse(eventRow.EndDate, out var parsedEnd) ? parsedEnd : start;
            var caseStatus = string.IsNullOrWhiteSpace(caseRow.CaseStatus) ? "Pipeline" : caseRow.CaseStatus;
            if (end < today || start > latest || caseStatus is "Resolved / Closed" or "Triage" || caseRow.Status is "Closed" or "Complete" or "Triage") continue;
            var additional = (namesByCase.GetValueOrDefault(caseRow.Id) ?? []).Where(name => !name.Equals(caseRow.AssignedAttorney, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (!string.IsNullOrWhiteSpace(attorney) && !attorney.Equals("all", StringComparison.OrdinalIgnoreCase) && !string.Equals(caseRow.AssignedAttorney, attorney, StringComparison.OrdinalIgnoreCase) && !additional.Any(name => name.Equals(attorney, StringComparison.OrdinalIgnoreCase))) continue;
            if (!string.IsNullOrWhiteSpace(division) && !division.Equals("all", StringComparison.OrdinalIgnoreCase) && !string.Equals(caseRow.Division, division, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add((caseRow, eventRow, string.Join(", ", additional), Math.Max(0, start.DayNumber - today.DayNumber)));
        }
        return result.OrderBy(row => row.Event.HearingDate).ThenBy(row => row.Event.Id).Select(row => request.Columns.ToDictionary(column => column.Key, column => column.Key switch
        {
            "trialDate" => row.Event.EndDate is not null && row.Event.EndDate != row.Event.HearingDate ? $"{row.Event.HearingDate} – {row.Event.EndDate}" : row.Event.HearingDate ?? "",
            "case" => string.IsNullOrWhiteSpace(row.Case.CaseName) ? row.Case.CaseNumber : row.Case.CaseName,
            "jobTract" => string.Join(" / ", new[] { row.Case.JobNumber, row.Case.Tract }.Where(value => !string.IsNullOrWhiteSpace(value))),
            "county" => row.Case.County,
            "primaryAttorney" => row.Case.AssignedAttorney ?? "Unassigned",
            "additionalAttorneys" => row.Additional,
            "days" => row.Days.ToString(),
            _ => ""
        })).ToList();
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
