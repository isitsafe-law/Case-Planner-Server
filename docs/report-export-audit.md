# Report export audit

The Reports page currently contains five report views. All five use the shared `ReportExportActions` client component and the existing server-side Excel writer at `/api/reports/export.xlsx`.

| Report | Rows exported | Active scope/filter values |
|---|---|---|
| Case List Export | Filtered case-list rows and selected columns | Status, county, district, search text, opened-date range, selected columns |
| Upcoming Trials | Active Jury Trial event rows | Horizon, primary/additional attorney, division |
| Caseload & Workload | Open cases in the selected attorney view | Selected attorney; open-case scope |
| Outcomes | Closed cases with deposit and final judgment data | Outcome-eligible scope |
| Cycle Time | Closed cases with filing and closed dates | Resolution-eligible scope |

## Export contract

- CSV and Excel are available through the same control and use the same column definitions and row set.
- Excel requests send a report identifier, case scope, report title, generated timestamp, filter metadata, columns, and filtered rows to the existing writer. The server validates the report identifier and rejects any scope containing a case outside the current user's visible-case set before writing the workbook. The writer does not query an unbounded dataset or add records beyond those already supplied by the report view.
- CSV includes a UTF-8 BOM, report title, generated timestamp, filter metadata, headers, and detail rows.
- Filenames use `CasePlanner_<Report>_<YYYY-MM-DD>.<extension>` with unsafe filename characters removed.
- The control disables duplicate submissions, shows a preparing state, and reports retryable failures inline.
- Report views are currently client-filtered and do not paginate their detail tables; exports therefore include the complete filtered in-memory result rather than a visible-page subset.

## Permission boundary

The report rows originate from the permission-filtered case/event data already loaded for the signed-in user. The Excel endpoint validates the report identifier and submitted case scope through `CaseAccessService`; it does not expand that scope. A future server-backed reporting API should accept the report identifier and filters instead of client-provided detail rows, re-run the permission-aware query, and retain this export contract.

## Work Queue date sources

- Ordinary checklist tasks and manual deadlines: direct inline edit.
- Generated/template deadlines: existing manual-override path; optional reason and history are preserved.
- Service deadlines: calculated from service basis/filing data; display-only in Work Queue.
- Discovery follow-up/deadline values: controlled by discovery records; display-only in the general Work Queue.
- Events/hearings: edited through the case Events workflow, not as ordinary Work Queue deadlines.

The case name remains the case-navigation link. Item-specific actions remain separate where they open or update the underlying work item.
