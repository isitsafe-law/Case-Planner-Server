# IT Handoff — Case Planner

## Current build

Case Planner currently has a portable SQLite test build for Windows. Extract the release ZIP to a writable directory and run `CasePlanner.Web.Server.exe`. The application creates `data`, `backups`, `exports`, `logs`, and template folders beside the executable.

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

Pipeline review and pre-filing sign-off are managed together in the consolidated blue Pre-filing Workflow card near the case header. It contains holder/context, waiting-on, next action, follow-up date, filing-gate state, milestone marking/unmarking, and review notes. Work contains deadlines and tasks. Events contains hearings, trials, depositions, mediation, meetings, inspections, and other scheduled proceedings. `cases.trial_date` remains the controlling jury-trial date and is displayed prominently.

Close/Reopen remains broadly available in the SQLite preview for testing. It preserves related work and audit history. Add Entra-based authorization before production use.

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
