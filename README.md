# Case Planner

Case Planner is an ARDOT condemnation case-management application for attorneys, legal assistants, administrators, and managing attorneys. The case is the primary work unit; a case may represent one tract within a larger job.

## Current status

The current test build is a portable Windows ASP.NET application with a React client and SQLite storage. Entra authentication and final manager-only authorization are not enabled in the SQLite preview.

The broad case workflow is:

- Pipeline: pre-filing assignment, drafting, review, revision, and signatures
- Filed / Service Pending
- Active Litigation
- Settlement Pending
- Trial Preparation
- Resolved / Closed

Imported historical cases may remain in Triage until intake is completed.

### Imported-case triage

Imported cases use one Triage and Activate screen. It reviews imported identifiers, assignment, case position, filing date, service status, optional discovery strategy, next action/follow-up, and optional checklist/deadline generation. `Save and Activate` performs the reviewed case save, stores discovery posture when supplied, records activation history, and generates only selected work. Service not perfected and an undecided discovery strategy are warnings, not ordinary activation blockers. The Excel importer remains unchanged.

## Portable test build

Use the release folder directly (or copy it to a writable location) and run `CasePlanner.Web.Server.exe`. Open the local HTTP address shown by the application. Keep the folder together; runtime folders are created beside the executable.

- `data/` — SQLite database
- `backups/` — database backups
- `exports/` — generated documents and reports
- `logs/` — application logs
- `templates/` — document and reference templates
- `import_samples/` — fictional demo import files

Do not put real case data in a test package without establishing backup and retention policies.

## Development and verification

Requirements are the .NET SDK used by the solution and Node.js/npm.

```powershell
cd client
npm install
npm run build
npm test -- --run

cd ..
dotnet build server/CasePlanner.Web.Server/CasePlanner.Web.Server.csproj --no-restore
.\scripts\publish-portable.ps1 -Output 'release/CasePlannerWeb_Portable_SQLite_Test_<date>'
.\scripts\local-package-smoke.ps1 -PackagePath 'release/CasePlannerWeb_Portable_SQLite_Test_<date>' -Port 5300
```

The package smoke test checks SQLite startup, the document-template catalog, and DOCX generation.

## Workflow

Pipeline cases expose one consolidated blue Pre-filing Workflow card near the case header. That card contains the current holder, the active milestone dates, milestone marking/unmarking, and review notes. Sign-off history is preserved in the pre-filing milestone/review records and activity history. The old holder/review controls are not separate header controls.

The milestone rows are intentionally compact and now focus on two core dates: Pleadings Package Sent and Chief Counsel Signatures Received. A stage can be marked complete without a note; `Add note` expands an optional note editor, while existing notes remain collapsed behind `Note available`. Unmarking still requires a reason. Current holder is changed directly in the card. Waiting-on, next-action, follow-up, and filing-gate fields are not routine pre-filing controls. Legacy milestone values remain preserved for history and internal compatibility but are not ordinary card prompts. The removed Director Signature Received milestone no longer blocks a case from leaving Pipeline. Administrative Actions is the final section of Edit Case.

Work is for deadlines and tasks. Events is for trials, hearings, depositions, mediation, meetings, inspections, and other scheduled proceedings. The controlling jury-trial date remains `cases.trial_date` and stays prominent in the case header. The next upcoming event may also appear in the header and drops off after it passes or is resolved.

Jury Trial is an Events event type and is the preferred editing path for trial dates. Events support optional end dates and start/end times for multi-day proceedings. Event status remains stored only for compatibility with older records; the active UI uses the event date range and deletion rather than Scheduled/Completed/Continued/Canceled controls. Pipeline views use Pipeline Stage only and do not fall back to litigation-stage values.

Close and Reopen are administrative actions inside Edit Case in the SQLite preview. They preserve tasks, deadlines, events, notes, documents, and audit history. Entra authorization is planned for a later deployment stage.

## Management dashboard

The Division Overview summarizes upcoming events, needs-attention cases, pipeline matters, and open tracts across the management scope. Open tracts include pipeline and filed work and exclude resolved/closed, legacy closed/complete, and Triage cases. Unassigned pipeline cases remain available through the pipeline data-quality/reporting views but are not a separate Division Overview card.

The By Attorney view reports transparent workload signals—open tracts, pipeline tracts, events in the next 30 days, overdue deadlines, and needs-attention cases—alongside status distribution. These are observational counts, not a permanent weighted score; a weighting formula should be adopted only after management review of real docket behavior.

The Division Overview's data-quality table includes representative affected-case links when the finding is case-specific. It shows up to three direct case links and a count of additional affected records; the full issue count remains the authoritative metric.
The same section can export all current findings, definitions, counts, suggested actions, and sample case IDs to CSV for management follow-up.

The top-level Calendar is the shared case-event view. It defaults to the signed-in attorney when Entra identity is available; SQLite preview mode provides an all-attorney view for testing. It supports 30/60/90/120/180-day and See All ranges, attorney scope, event-type filters, multi-day events, and links back to cases. Events are intentionally not Work Queue items; Work Queue contains tasks, deadlines, discovery, and service work. The manager calendar uses the same event catalog and permission-filtered event feed.

Jury trial dates remain the controlling case-level date for the header and trial-watch views. A Jury Trial event is synchronized when edited through Events; other proceedings are stored in the hearings event catalog. The data-quality report flags conflicting trial representations for review rather than silently choosing a date.

Planned Work is priority level 4 in the attorney Action Queue. It is an observational bucket for open filed-case work that is appropriate to schedule or advance but is not an immediate deadline, attorney decision, discovery issue, or stale-momentum concern. It is not a second task list and does not create work by itself; the underlying case signal and review date remain the source of truth.

The manager dashboard does not show a permanent Awaiting Triage card. Triage is surfaced conditionally in the attorney workflow only when triage cases exist.

The By Attorney view's Next Hard Date uses open case deadlines, jury-trial dates, and scheduled Hearing/Deposition/Mediation/Filing Deadline events. Completed or canceled events, generic Other events, tasks, and pipeline follow-up dates are not substituted as hard dates. The display includes the date and its label.

Service-pending alerts use graduated bands from the filing date: day 60 is an attorney check-in, day 90 begins management-visible developing risk, days 105 and 115 increase urgency, and day 120 is due/overdue. Pipeline, closed, and perfected matters are excluded. The manager Needs Attention list does not promote ordinary 60-day check-ins.

## County and publication references

County Officials are county-linked reference data. The compact card is collapsible and retains copy actions for individual officials and the combined reference block. Circuit clerks, assessors, collectors, newspapers, addresses, phones, and emails are stored separately from the case record.

## Templates and merge tags

Document templates are stored in the application and generated as drafts for user review. Merge tags use `{{Token}}` syntax. The catalog is exposed through `/api/template-tags` and resolved by `DocumentGenerationEngine`.

Appearance settings include light/dark, high-contrast, pastel, deep navy, forest, slate, sunset, rose, ocean, plum, amber, carbon, and arctic themes. Theme choice is stored locally per browser; semantic warning/success/danger meanings remain consistent across variants.

Known and unknown missing values do not block generation. They produce a `[MISSING: Token]` marker and are reported to the caller. Existing legacy tokens remain available for compatibility while newer hierarchical tokens are added and tested.

Basic full-style construction uses:

```text
Arkansas State Highway Commission v. <party names>
```

Stored `CaseStyle` remains authoritative when present. Multi-party output should use party entities rather than assuming a single landowner or opposing-counsel field.

The current SQLite UI uses the existing ordered defendant/interest-holder rows as the first canonical party list and offers `Save Case Style` / `Rebuild from Parties`. Each row now carries a small designation (`Defendant`, `Unknown Heirs`, `Lienholder`, `Tenant`, or `Other`), with legacy rows defaulting to `Defendant`. Move Up/Move Down controls change the stored order used by case-style construction. This is intentionally additive: legacy owner/landowner values remain available as fallback data.

## Checklist and deadline rules

Work-item templates use broad case statuses and optional stage groupings. The legacy database field `phase` is retained for compatibility and does not imply a finely divided workflow.

Supported deadline anchors include filing date, jury trial date, date opened, date of taking, service perfected date, answer filed date, and closed date. Missing anchors skip calculation rather than inventing a date. Generated items retain source provenance and manual changes.

## Testing expectations

Important coverage includes consolidated triage activation, optional discovery strategy persistence, service-alert date bands, conditional triage rendering, management totals, pipeline sign-off, Close/Reopen retention, Events navigation, jury-trial/header behavior, County Officials collapse behavior, merge-tag resolver completeness, missing-tag warnings, checklist/deadline anchors, and portable package startup.

The Diagnostics settings page now exposes Portable Validation. It checks database/write safety, backup/export/log folders, active document-template paths, and critical data-quality findings. The adjacent **Test Backup / Restore** action creates a fresh SQLite backup, runs `PRAGMA integrity_check`, verifies required schema tables, and opens a temporary restored copy without replacing the live database. `/api/data-quality` returns stable checks for unassigned pipeline cases, missing case styles, missing party records, conflicting jury-trial representations, and missing template files. `/api/portable-validation` is the portable deployment contract that can later be reused by the server/IT health checks; `POST /api/portable-validation/backup-restore` is the safe local recovery validation contract.

The Calendar page now uses the paged `/api/calendar/events` endpoint with server-side date-range, event-type, and attorney filtering. The page displays 100 events at a time with Previous/Next controls, while the existing work queues and manager summaries continue to use their broader feeds.

Document-generation failures include a request ID in the user-facing error and in the portable log entry. Use that ID with the latest log path shown in Diagnostics when investigating a failed generation or an HTTP 500.

The automated server suite also includes a portable upgrade fixture. It removes representative newer SQLite columns/tables from a throwaway database, reruns normal startup initialization, verifies legacy case and opposing-counsel data survives, and generates a current DOCX afterward. This is a test-only fixture; it never modifies a user database.

API responses include an `X-Request-Id` correlation header. If a portable request fails unexpectedly, include that ID with the latest log when reporting the problem.

The provider-neutral `/api/calendar/events` endpoint also exposes date, event-type, attorney, limit, and offset parameters for the next calendar pagination pass. The current SQLite client continues to use the compatibility feed while the paged response is validated.

The document-generation scenarios and expected outcomes are recorded in [docs/document-generation-test-matrix.md](docs/document-generation-test-matrix.md).

Document templates also expose `/api/document-platform/templates/{key}/completeness`. It audits the active DOCX for registered tags, declared runtime-input tags, and unknown tags before a future server cutover. Tag matching is case-insensitive, so legacy templates using forms such as `{{COUNTY}}` continue to resolve to the canonical `County` field. Blank values remain visible as missing data during generation rather than blocking the document.

## Deferred work

- Entra authentication and final manager/admin authorization
- Trial-event source-of-truth migration; `cases.trial_date` remains authoritative
- Weighted workload scoring
- Final confidential settlement/authority tag policy
- Production deployment and network-share storage policy

An actual restore now returns the restored backup name, the automatically created pre-restore safety backup name, and the restore timestamp so the recovery action is auditable in the UI.

Data-quality findings expose a total count plus the number of additional affected cases beyond the navigable sample links, so exports and manager reports do not imply that the first few links are the complete finding set.

## Case record and trial-event notes

Case Style is displayed in the lower Case Record section rather than the top Overview area. Its stored caption, document-generation behavior, and existing edit/rebuild/copy actions are unchanged.

On startup, legacy nonblank `cases.trial_date` values are reconciled into a Jury Trial event when no such event exists. The one-time migration preserves the legacy case-date projection and records conflict or multiple-event cases for review. Jury Trial event edits continue to synchronize the case-level compatibility dates; deleting the selected Jury Trial synchronizes them to the next remaining event or clears them when no replacement exists.

The current SQLite model retains the legacy primary assigned-attorney projection and explicit case legal-assistant rows while the additive attorney-assignment relation is adopted. The canonical Service Log party reference is optional; historical free-text service party names are preserved and no destructive consolidation is performed automatically.

Service Log now has an additive nullable reference to `case_defendants`. New entries can select a canonical case party while preserving the party-name snapshot; older free-text entries are not automatically rewritten. The SQL Server runtime store now supports the same bridge, with deployment validation still deferred.

Case attorney assignments now have an additive SQLite/API foundation with `Primary` and `Supporting` roles and an Edit Case control. The legacy `cases.assigned_attorney` field remains the compatibility projection for dashboards, calendars, permissions, and exports until those consumers are migrated together.

On startup, a one-time SQLite migration backfills every existing nonblank `cases.assigned_attorney` value into a `Primary` assignment row. The legacy field remains intact, so existing reports and integrations continue to work while the assignment relation is adopted.

The open-case Work tab task assignee picker now includes supporting attorneys for the currently open case, alongside the primary attorney and legal assistants. Cross-case queue options continue to use the existing compatibility feed until assignment rows are included in that feed.

The client now loads all SQLite attorney-assignment rows through `/api/attorney-assignments` and includes supporting attorneys in cross-case Work Queue assignee options. If a future provider does not yet implement the endpoint, the client falls back to the legacy primary-attorney/legal-assistant feed.

Caseload reporting now recognizes supporting attorneys for attorney filtering, per-attorney case rows, trial density, deadline density, and workload totals. A case can therefore appear under more than one attorney row; the primary assignment remains the displayed case owner.

The global Calendar endpoint now matches an attorney filter against both the primary case assignment and supporting attorney rows. The event payload still displays the primary attorney as the case owner; SQL Server falls back to primary-only filtering until its assignment store is implemented.

Attorney assignment changes are recorded in the existing case activity stream as `AttorneyAssignmentChanged` and `AttorneyAssignmentRemoved`, including the assignment name/role and actor metadata. No separate audit table is introduced.

The SQL Server attorney-assignment store now follows the provider pattern used by other case child records: reads, inserts, updates with row-version concurrency, soft deletes, and audit events are implemented. The SQLite path remains the portable test-build source.

Data-quality reporting now flags orphaned or duplicate attorney assignments and invalid Service Log-to-defendant references. These findings are review-only; no automatic backfill or deletion is performed.

It also flags assignment names that do not match an active Staff Directory attorney, including legacy or deactivated names that need deliberate review before a future identity migration.

It also flags cases whose legacy primary-attorney projection has no matching `Primary` assignment row, which helps catch imported or newly created records that need reconciliation.

Diagnostics findings can be exported to CSV with their definitions, suggested actions, counts, and sample case IDs for review or IT handoff.

Document-generation tests now audit every active built-in template for unknown merge tags, in addition to testing missing-value markers, optional sections, repeated generations, and portable template-path repair.

If document generation fails unexpectedly, the portable server retains the latest failure message, operation, request ID, and log path in Diagnostics. This makes a reported 500 actionable without server access; the full exception remains in the local log.

Malformed or unreadable DOCX templates now return a specific template error to the user instead of an opaque server error.

Data Quality also flags active document templates that contain unknown merge tags, so template drift is visible before a user attempts generation.

The latest recorded generation failure is cleared automatically after a successful generation, keeping Diagnostics focused on unresolved problems.

The portable Diagnostics page now displays active data-quality findings and supports refreshing them. Assignment and Service Log integrity checks are visible there alongside the existing backup, write-safety, and document-template diagnostics.

Case list rows and open-case headers now show the primary attorney separately from supporting attorneys, keeping the view compact while making shared responsibility visible.

Caseload reporting labels its headline as unique open cases and explains that per-attorney rows count assignments; a shared case may therefore appear under multiple attorneys without inflating the unique-case headline.

Event reconciliation is observational: data-quality checks flag a case-level jury-trial date with no matching calendar event, a Jury Trial event with no case-level date, and conflicting dates. The case-level `trial_date` remains authoritative until a deliberate source-of-truth migration is approved.

When behavior changes, update this README and the IT handoff documentation in the same change.
