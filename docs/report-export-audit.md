# Report export audit

The Reports page currently contains five report views. All five use the shared `ReportExportActions` client component and the existing server-side Excel writer at `/api/reports/export.xlsx`.

| Report | Rows exported | Active scope/filter values |
|---|---|---|
| Case List Export | Filtered case-list rows and selected columns | Status, county, district, search text, opened-date range, selected columns |
| Upcoming Trials | Active Jury Trial event rows queried by the server | Horizon, primary/additional attorney, division |
| Caseload & Workload | Open cases in the selected attorney view | Selected attorney; open-case scope |
| Outcomes | Closed cases with deposit and final judgment data queried by the server | Outcome-eligible scope |
| Cycle Time | Closed cases with filing and closed dates queried by the server | Resolution-eligible scope |

## Export contract

- CSV and Excel are available through the same control and use the same column definitions and row set.
- Excel requests send a report identifier, case scope, report title, generated timestamp, filter metadata, columns, and filtered rows to the existing writer. The server validates the report identifier and rejects any scope containing a case outside the current user's visible-case set before writing the workbook. The writer does not query an unbounded dataset or add records beyond those already supplied by the report view.
- CSV includes a UTF-8 BOM, report title, generated timestamp, filter metadata, headers, and detail rows.
- Filenames use `CasePlanner_<Report>_<YYYY-MM-DD>.<extension>` with unsafe filename characters removed.
- The control disables duplicate submissions, shows a preparing state, and reports retryable failures inline.
- Report views are currently client-filtered and do not paginate their detail tables; exports therefore include the complete filtered in-memory result rather than a visible-page subset.

## Permission boundary

Case List, Upcoming Trials, Outcomes, and Cycle Time Excel exports now send their report identifiers and filters to the server. The server re-queries the provider-neutral case catalog/hearing stores, reapplies each report's eligibility rules, and writes the workbook from those results. Caseload still sends its already-filtered detail rows, while the endpoint validates its report identifier and submitted case scope through `CaseAccessService`; it does not expand that scope. Its workload/assignment query contract can move server-side in the same pattern as provider-neutral report services are added.

## Work Queue date sources

- Ordinary checklist tasks and manual deadlines: direct inline edit.
- Generated/template deadlines: existing manual-override path; optional reason and history are preserved.
- Service deadlines: calculated from service basis/filing data; display-only in Work Queue.
- Discovery follow-up/deadline values: controlled by discovery records; display-only in the general Work Queue.
- Events/hearings: edited through the case Events workflow, not as ordinary Work Queue deadlines.

The case name remains the case-navigation link. Item-specific actions remain separate where they open or update the underlying work item.
