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

The Division Overview summarizes upcoming events, needs-attention cases, pipeline matters, unassigned pipeline matters, and open tracts across the management scope. Open tracts include pipeline and filed work and exclude resolved/closed, legacy closed/complete, and Triage cases. The open-tract display provides pipeline, filed, unassigned, and needs-attention context.

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

## Checklist and deadline rules

Work-item templates use broad case statuses and optional stage groupings. The legacy database field `phase` is retained for compatibility and does not imply a finely divided workflow.

Supported deadline anchors include filing date, jury trial date, date opened, date of taking, service perfected date, answer filed date, and closed date. Missing anchors skip calculation rather than inventing a date. Generated items retain source provenance and manual changes.

## Testing expectations

Important coverage includes consolidated triage activation, optional discovery strategy persistence, service-alert date bands, conditional triage rendering, management totals, pipeline sign-off, Close/Reopen retention, Events navigation, jury-trial/header behavior, County Officials collapse behavior, merge-tag resolver completeness, missing-tag warnings, checklist/deadline anchors, and portable package startup.

## Deferred work

- Entra authentication and final manager/admin authorization
- Trial-event source-of-truth migration; `cases.trial_date` remains authoritative
- Weighted workload scoring
- Final confidential settlement/authority tag policy
- Production deployment and network-share storage policy

When behavior changes, update this README and the IT handoff documentation in the same change.
