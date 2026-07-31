# IT Handoff — Case Planner

## Current build

Case Planner currently has a portable SQLite test build for Windows. Copy the release folder to a writable directory and run `CasePlanner.Web.Server.exe`. The application creates `data`, `backups`, `exports`, `logs`, and template folders beside the executable.

This is a test/preview deployment. Entra authentication and manager-only authorization are not yet enabled.

## First-machine checklist

1. Confirm the folder is writable.
2. Start the executable and open its local HTTP address.
3. Confirm health, the document-template catalog, and sample DOCX generation.
4. Create a backup before importing, resetting, or restoring data.
5. Do not copy a developer database into a handoff package.
6. Establish the future backup location and retention policy.

## Data locations

- `data/`: SQLite database
- `backups/`: database backups
- `exports/`: generated documents and reports
- `logs/`: runtime logs
- `templates/`: document and reference templates

## Workflow notes

Pipeline review and pre-filing sign-off are managed together in the consolidated blue Pre-filing Workflow card near the case header. It contains the current holder and the two active milestone dates: Pleadings Package Sent and Chief Counsel Signatures Received. Marking supports optional notes; unmarking requires a reason. Waiting-on, next-action, follow-up, and filing-gate fields are not routine pre-filing controls. Legacy milestone values remain preserved for history, but the removed Director Signature Received milestone no longer blocks leaving Pipeline. Administrative Actions is the final section of Edit Case. Work contains deadlines and tasks. Events contains hearings, trials, depositions, mediation, meetings, inspections, and other scheduled proceedings. `cases.trial_date` remains the controlling jury-trial date and is displayed prominently.

Pipeline milestone rows are compact and focus on Pleadings Package Sent and Chief Counsel Signatures Received. Marking can proceed without a note; `Add note` opens the optional note editor. Existing notes remain collapsed, and unmarking requires a reason. Current holder is changed directly in the card. Waiting-on, next-action, follow-up, and filing-gate fields are no longer routine pre-filing controls. Legacy milestone values remain preserved for history and internal compatibility. The By Attorney `Next Hard Date` excludes completed/canceled events, generic Other events, tasks, and pipeline follow-up dates; it considers open deadlines, jury trials, and scheduled legal/proceeding events.

Close/Reopen remains broadly available in the SQLite preview for testing. It preserves related work and audit history. Add Entra-based authorization before production use.

Events now use Jury Trial as the only trial event type and support optional end dates plus start/end times. The ordinary event form no longer exposes event status; legacy status values remain stored for compatibility, while date ranges and deletion drive active visibility. The Work tab no longer edits the competing case-level jury date. Pipeline displays use Pipeline Stage rather than falling back to litigation stage values, and Pipeline/Triage work generation remains blocked for ordinary litigation templates.

The top-level Calendar is the shared case-event view. It supports attorney scope, 30/60/90/120/180-day and See All ranges, event-type filters, multi-day events, and case navigation. It uses the permission-filtered event feed used by management views. Events are not included in Work Queue or the dashboard's due-work list; Work Queue remains for actionable tasks, deadlines, discovery, and service items. Outlook/Graph availability and Entra-based manager authorization remain deferred.

Division Overview no longer shows a separate Unassigned Pipeline card or the old 30,000-foot-view tagline. Unassigned records remain available in pipeline/reporting data-quality views, where representative affected cases can now be opened directly and the full findings list can be exported to CSV. Planned Work is priority level 4 in the attorney Action Queue: an observational planning bucket for open work that is not currently immediate, decision-required, discovery-blocked, or stale.

The By Attorney view now shows transparent workload signals: open tracts, pipeline tracts, events in the next 30 days, overdue deadlines, and needs-attention cases. No weighted workload formula has been hard-coded; that decision remains a management calibration task.

Imported cases use a consolidated Triage and Activate screen. A single `Save and Activate` action saves reviewed fields, optionally stores discovery strategy, records activation, and generates only selected checklist/deadline templates. Service not perfected and discovery strategy deferred are warnings, not ordinary activation blockers. The working Excel importer is intentionally unchanged.

Service-pending behavior is graduated: day 60 is an attorney check-in; day 90 begins management-visible risk; days 105/115 are high and urgent bands; day 120 is due/overdue. Pipeline, closed, and perfected cases are excluded from filed-case service alerts. The manager Needs Attention view does not elevate routine day-60 cases.

Appearance options now include pastel blue/sage/lavender, deep navy, forest, slate, sunset, rose, ocean, plum, amber, carbon, and arctic variants in addition to light, dark, and high contrast. These are browser-local preferences and do not change data or deployment settings.

The ordered party rows used for case-style construction now include a small designation: Defendant, Unknown Heirs, Lienholder, Tenant, or Other. Existing rows migrate to Defendant. Move Up/Move Down controls persist the order used by case-style construction, and the dashboard remains compact rather than listing every party.

Diagnostics now includes Portable Validation. It checks SQLite write safety, backup/export/log folders, active document-template paths, and critical data-quality findings. **Test Backup / Restore** creates a backup, checks SQLite integrity and required tables, and opens a temporary restored copy without replacing the live database. The `/api/data-quality`, `/api/portable-validation`, and `POST /api/portable-validation/backup-restore` contracts are intentionally provider-neutral enough to reuse when the server implementation is introduced. Jury trial dates remain the controlling case-level date for header/trial-watch behavior; Jury Trial event edits synchronize that compatibility projection, while other proceedings use the hearings catalog.

The global Calendar page consumes `/api/calendar/events` with server-side range, event-type, and attorney filters and 100-row pagination. This is the intended provider-neutral contract for a future server-backed calendar; the manager summary views retain their existing division-wide data feed for aggregate calculations.

## Document generation

Templates use `{{Token}}` merge tags. The server catalog and resolver are maintained together. Missing or unknown values produce a missing marker/warning and do not block draft generation. Users must review generated drafts before they are passed along or filed. If generation fails unexpectedly, the response and the log contain the same request ID so IT can correlate the user report with the portable log.

The server test suite includes an isolated portable upgrade fixture that exercises startup migrations against a deliberately older SQLite shape, verifies representative case and party data preservation, and confirms current DOCX generation remains available after the upgrade. It does not alter a live or packaged user database.

## Verification

```powershell
cd client
npm test -- --run
npm run build

cd ..
dotnet build server/CasePlanner.Web.Server/CasePlanner.Web.Server.csproj --no-restore
.\scripts\local-package-smoke.ps1 -PackagePath '<portable-package>' -Port 5300
```

Document-generation diagnostics also expose `/api/document-platform/templates/{key}/completeness`. This checks the active template's discovered merge tags against the canonical registry and its declared runtime inputs. Unknown tags are reported for correction, while empty case values remain non-blocking and render as missing markers. Tag matching is case-insensitive for compatibility with older attorney-authored templates.

## Known deferred items

- Entra authentication and final permissions
- Trial-event source-of-truth migration
- Weighted workload scoring
- Final policy for confidential settlement/authority merge tags
- Production deployment and network-share storage policy

An actual restore reports the selected backup and the automatically created pre-restore safety backup. Keep that safety backup until the restored database has been reviewed.

Manager data-quality findings now distinguish the total affected-case count from the small sample of direct case links shown in the UI. This keeps the portable report actionable without loading every affected case into the dashboard at once.

The report also flags incomplete jury-trial synchronization without changing dates automatically. `cases.trial_date` remains authoritative; the hearings catalog supplies shared calendar events until a future source-of-truth migration is deliberately approved.

Keep this file synchronized with `README.md` when release, workflow, or storage behavior changes.

## Current case-record migration notes

- Case Style now appears in the lower Case Record card. Its stored value and document/export behavior are unchanged.
- Startup performs a one-time, additive reconciliation of legacy case trial dates into Jury Trial events. Conflicting or multiple Jury Trial representations are counted and left for review; existing dates are not discarded.
- Jury Trial edits keep the case-level trial-date projection synchronized. Deleting a Jury Trial selects the next remaining Jury Trial when available, otherwise clears the compatible case dates.
- Primary attorney assignment and multiple legal-assistant rows remain the current SQLite source of truth. Supporting attorney assignments remain staged work; historical free-text service party names remain preserved while the new optional Service Log-to-defendant bridge is adopted.
- SQLite and SQL Server Service Log entries support an optional `case_defendant_id`; the UI can select a canonical party, and the original party name remains preserved as a snapshot. Existing entries are not mass-rewritten. SQL Server deployment validation remains deferred.
- SQLite now has an additive `case_attorney_assignments` relation and API/Edit Case controls for `Primary` and `Supporting` attorney rows. The legacy assigned-attorney field remains authoritative to existing consumers until the provider-neutral compatibility read migration is complete; SQL Server runtime assignment storage remains staged.
- The open-case Work task assignee picker includes supporting attorneys. Cross-case queue assignment options still use the legacy primary-attorney/legal-assistant compatibility feed pending a paged assignment-aware endpoint.
- SQLite now exposes `/api/attorney-assignments` for the assignment-aware queue feed. The client groups those rows by case and falls back safely when a provider does not implement the endpoint.
- Caseload reporting recognizes supporting assignments for attorney filters and per-attorney case, trial, and deadline counts. These are assignment counts, so one case may contribute to multiple attorney rows; primary ownership is unchanged.
- Calendar attorney filtering now matches supporting assignments on SQLite while retaining the primary attorney as the displayed case owner. SQL Server remains primary-only until its assignment runtime store is implemented.
- Attorney assignment additions/changes/removals are recorded in the existing activity log with assignment name/role and actor metadata. This is the audit source for the current SQLite implementation.
- SQL Server attorney-assignment storage now has a provider implementation with row-version concurrency, soft delete, and audit-event writes. It still requires deployment validation against an actual SQL Server environment.
