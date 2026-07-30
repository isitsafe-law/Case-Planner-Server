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

## Portable test build

Extract the release ZIP to a writable folder and run `CasePlanner.Web.Server.exe`. Open the local HTTP address shown by the application. Keep the extracted folder together; runtime folders are created beside the executable.

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

Pipeline cases expose one consolidated blue Pre-filing Workflow card near the case header. That card contains the current holder/context, waiting-on note, next action, follow-up date, filing-gate state, milestone marking/unmarking, and review notes. Sign-off history is preserved in the pre-filing milestone/review records and activity history. The old holder/review controls are not separate header controls.

Work is for deadlines and tasks. Events is for trials, hearings, depositions, mediation, meetings, inspections, and other scheduled proceedings. The controlling jury-trial date remains `cases.trial_date` and stays prominent in the case header. The next upcoming event may also appear in the header and drops off after it passes or is resolved.

Close and Reopen are administrative actions inside Edit Case in the SQLite preview. They preserve tasks, deadlines, events, notes, documents, and audit history. Entra authorization is planned for a later deployment stage.

## Management dashboard

The Division Overview summarizes upcoming events, needs-attention cases, pipeline matters, unassigned pipeline matters, and open tracts across the management scope. Open tracts include pipeline and filed work and exclude resolved/closed, legacy closed/complete, and Triage cases. The open-tract display provides pipeline, filed, unassigned, and needs-attention context.

The manager dashboard does not show a permanent Awaiting Triage card. Triage is surfaced conditionally in the attorney workflow only when triage cases exist.

## County and publication references

County Officials are county-linked reference data. The compact card is collapsible and retains copy actions for individual officials and the combined reference block. Circuit clerks, assessors, collectors, newspapers, addresses, phones, and emails are stored separately from the case record.

## Templates and merge tags

Document templates are stored in the application and generated as drafts for user review. Merge tags use `{{Token}}` syntax. The catalog is exposed through `/api/template-tags` and resolved by `DocumentGenerationEngine`.

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

Important coverage includes conditional triage rendering, management totals, pipeline sign-off, Close/Reopen retention, Events navigation, jury-trial/header behavior, County Officials collapse behavior, merge-tag resolver completeness, missing-tag warnings, checklist/deadline anchors, and portable package startup.

## Deferred work

- Entra authentication and final manager/admin authorization
- Trial-event source-of-truth migration; `cases.trial_date` remains authoritative
- Weighted workload scoring
- Final confidential settlement/authority tag policy
- Production deployment and network-share storage policy

When behavior changes, update this README and the IT handoff documentation in the same change.
