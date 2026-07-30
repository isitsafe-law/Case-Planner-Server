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

Imported cases use a consolidated Triage and Activate screen. A single `Save and Activate` action saves reviewed fields, optionally stores discovery strategy, records activation, and generates only selected checklist/deadline templates. Service not perfected and discovery strategy deferred are warnings, not ordinary activation blockers. The working Excel importer is intentionally unchanged.

Service-pending behavior is graduated: day 60 is an attorney check-in; day 90 begins management-visible risk; days 105/115 are high and urgent bands; day 120 is due/overdue. Pipeline, closed, and perfected cases are excluded from filed-case service alerts. The manager Needs Attention view does not elevate routine day-60 cases.

Appearance options now include pastel blue/sage/lavender, deep navy, forest, slate, sunset, rose, ocean, plum, amber, carbon, and arctic variants in addition to light, dark, and high contrast. These are browser-local preferences and do not change data or deployment settings.

## Document generation

Templates use `{{Token}}` merge tags. The server catalog and resolver are maintained together. Missing or unknown values produce a missing marker/warning and do not block draft generation. Users must review generated drafts before they are passed along or filed.

## Verification

```powershell
cd client
npm test -- --run
npm run build

cd ..
dotnet build server/CasePlanner.Web.Server/CasePlanner.Web.Server.csproj --no-restore
.\scripts\local-package-smoke.ps1 -PackagePath '<portable-package>' -Port 5300
```

## Known deferred items

- Entra authentication and final permissions
- Trial-event source-of-truth migration
- Weighted workload scoring
- Final policy for confidential settlement/authority merge tags
- Production deployment and network-share storage policy

Keep this file synchronized with `README.md` when release, workflow, or storage behavior changes.
