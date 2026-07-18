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

## Build order

- Phase 1: shared open-case and upcoming-work selectors, plus tests against existing Work Queue behavior.
- Phase 2: compact dashboard upcoming-work view with 5/10 preference, filters, actions, and Work Queue navigation.
- Phase 3: Reports navigation, builder, preview, saved definitions, seeded reports, and exports.
- Phase 4: migration/backup verification and portable deployment.

## Deployment decision

Publish explicitly for `win-x64` with `SelfContained=true`. Prefer single-file output; if native SQLite or file-relative template behavior prevents it, use a self-contained portable folder and document the reason. In both cases the release must carry `data`, `backups`, `exports`, `templates`, and `logs` beside the executable and must not ship a replacement production database.
