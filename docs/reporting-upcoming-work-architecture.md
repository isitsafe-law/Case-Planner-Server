# Reporting and Upcoming Work Architecture

## Current architecture

- Cases are read through `CasePlannerRepository`; the consolidated `case_status` projection is already used by dashboard and checklist/deadline generation while legacy `status`, `stage`, and `track` remain compatibility fields.
- Work Queue data is exposed through the existing deadline, checklist, discovery, service, and hearing repository queries and loaded by `App.tsx` for the current global queue.
- Case Insight and Attorney Action Queue use `GetAttorneyDashboardAsync` and its shared dashboard engines for judgment-oriented work.
- Exports currently use repository-owned export paths and ClosedXML/OpenXML services; there is no general saved-report definition or report DTO yet.
- The current publish project is framework-dependent by default: it has no explicit runtime identifier, `SelfContained`, or single-file settings. Frontend assets are built separately and copied into the publish folder.

## Proposed shared model

1. Define one backend `IsOpenCase` rule: `Pipeline`, `Filed / Service Pending`, `Active Litigation`, `Settlement Pending`, and `Trial Preparation` are open; `Resolved / Closed`, `Triage`, deleted, and archived rows are excluded unless a query explicitly opts in.
2. Add a shared `UpcomingWorkItem` projection/query that composes existing work-item sources, applies case eligibility, deferment, completion, urgency, type, sort, and limit rules, and returns only 5 or 10 rows.
3. Add report-specific filter/column/group DTOs and saved definitions backed by migrations. Reports remain read-only and reuse the same case/work selectors.
4. Add Excel/CSV report services over the projection, preserving numeric/date types in Excel and stable raw values in CSV.

Saved report definitions are now available in the portable SQLite build. They are stored as one versioned
JSON application setting (`saved_report_definitions_v1`) and contain the report name, case filters, selected
columns, and sort order. This keeps the feature portable while the application is single-user. A future
SQL/Entra migration can promote these records to user- or division-scoped rows without changing the report
definition shape.
Loading a saved definition retains its identifier so refinements can update the existing definition; users
can explicitly switch to Save as new when they want a separate report.
The report builder also supports optional grouping by any selected column; grouping is a presentation choice
and does not alter counts, filters, exports, or the underlying case records.
Saved definitions can also retain a relative opened-date preset such as Last 30 days or This calendar year;
the dates are recalculated when the report is loaded so recurring reports do not become stale.
The report builder now includes three non-persistent starting views: Pipeline review, Upcoming trials, and
Open workload by attorney. They are ordinary report definitions that can be adjusted and then saved by the
user; they do not create duplicate records or change case data.

## Build order

- Phase 1: shared open-case and upcoming-work selectors, plus tests against existing Work Queue behavior.
- Phase 2: compact dashboard upcoming-work view with 5/10 preference, filters, actions, and Work Queue navigation.
- Phase 3: Reports navigation, builder, preview, saved definitions, seeded reports, and exports. The first
  saved-definition, presentation-grouping, relative-date, and seeded starting-view slices are complete;
user-scoped sharing remains follow-up work.

Portable backup/restore validation now checks the saved-report setting in the temporary restored copy. This
confirms that report definitions are carried with the SQLite database and that malformed saved-report JSON is
reported as a validation failure before a future migration.
The same restore check also runs SQLite foreign-key consistency validation, giving portable upgrade testing
a data-integrity signal in addition to file integrity and required-table checks.
- Phase 4: migration/backup verification and portable deployment.

## Deployment decision

Publish explicitly for `win-x64` with `SelfContained=true`. Prefer single-file output; if native SQLite or file-relative template behavior prevents it, use a self-contained portable folder and document the reason. In both cases the release must carry `data`, `backups`, `exports`, `templates`, and `logs` beside the executable and must not ship a replacement production database.
