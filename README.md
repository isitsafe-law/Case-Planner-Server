# ARDOT Legal Division Case Planner Web

Case Planner currently runs as a local web application and is beginning a staged migration toward a
centrally hosted, multi-user architecture using:

- React + TypeScript + Vite for the client
- ASP.NET Core Web API for the server
- SQLite for the current runtime data store
- SQL Server as the new migration target

The current server remains bound to `localhost`. It is **not ready to expose to a network**: authentication,
authorization, per-user case assignment, concurrency protection, and SQL Server repository cutover are
still required. See [SQL Server migration foundation](docs/sql-server-migration.md).

## Local project folders

- `server/CasePlanner.Web.Server`: ASP.NET Core backend
- `client`: React frontend
- `data`: local SQLite database
- `backups`: automatic backups before writes
- `exports`: generated DOCX outputs
- `templates`: unified document platform template source files (`templates/documents/platform`)
- `logs`: local diagnostics and startup logs
- `import_samples`: harmless import examples

## Current Phase 1.5 scope

- dashboard focused on needs attention
- cases list page and selected-case workspace
- selected-case tabs for Overview, Case Details, Deadlines, Checklist, Discovery, and Documents / QA
- global work queues
- service-focused reminders around the 120-day service deadline
- service queue and service status card in the selected-case workspace
- publication facts retained as supporting service detail instead of the primary warning headline
- light and dark theme toggle with local browser persistence
- top navigation consolidated to Dashboard, Cases, Work Queues, Documents, and Settings
- Settings now includes Appearance, Import, Diagnostics, Storage / Paths, and About / IT Notes sections
- in-page modal editors for case creation, deadline editing, checklist editing, and discovery editing
- Arkansas county dropdown shared across case creation, editing, and case filters
- CSV import browse button inside Settings
- clearer read-only path displays with copy buttons for local browser-visible paths
- more compact quick actions on Case Overview
- Case Overview emphasizes deposit amount, date of taking, service timing, next action, and workload summary
- local CSV import
- sample CSV import template for local testing
- DOCX case summary and case review memo generation
- local diagnostics and health endpoints
- write safety and backup-before-write
- friendly duplicate issue-tag validation
- CSV-only import messaging with XLSX deferment clearly documented
- self-contained Windows test packaging for localhost deployment review

## Development

Development/build dependencies:

- .NET 10 SDK (the projects target `net10.0`)
- Node.js 20 or newer and npm
- npm packages listed in `client/package.json`
- ASP.NET Core 10 runtime (included with the SDK for development)
- React + Vite frontend
- SQLite native runtime packages restored through NuGet
- SQL Server 2019 or newer for migration/cutover development
- NuGet packages `Microsoft.Data.Sqlite`, `SQLitePCLRaw.bundle_e_sqlite3`, and `Microsoft.Data.SqlClient`
- Microsoft Entra dependencies `Microsoft.Identity.Web`, `@azure/msal-browser`, and `@azure/msal-react`
- PowerShell for the documented Windows commands and publish script

The repository solution is CasePlanner.slnx. For a repeatable Phase 1 validation run, execute
scripts/phase1-smoke.ps1 using the existing restored assets. On a clean machine with NuGet access, add
the `-Restore` switch first (for example, `powershell -ExecutionPolicy Bypass -File .\scripts\phase1-smoke.ps1 -Restore`).
The first-machine handoff sequence is documented in `docs/it-first-machine-checklist.md`.
The extracted local test package includes `verify-local.ps1` for a repeatable health/catalog/DOCX smoke check.
The SQL Server/Entra cutover settings are provided separately in
`server/CasePlanner.Web.Server/appsettings.SqlServer.example.json`; the normal example remains SQLite-first.
The unified document platform (templates, versions, section/loop content, runtime inputs, and generation
history, all DB-backed) is documented in `docs/it-deployment-handoff.md` and `docs/sql-server-migration.md`
(migrations 023-026). Case-level document generation now uses `/api/document-platform/templates`,
`GET /api/cases/{id}/document-platform/templates/{key}/checklist`, and
`POST /api/cases/{id}/document-platform/templates/{key}/generate`; the Documents tab shows a section
checklist pre-checked from the case's issue tags, freely togglable, with overlap warnings, and merges
templates natively into `.docx` files with no third-party templating dependency.

Validation note: when NuGet is unavailable, `scripts/phase1-smoke.ps1 -WebOnly` validates the server
build/publish path only. A previously built server test assembly can be run directly with `dotnet vstest`,
but rebuilding the test and database-migrator projects requires NuGet access or an internal package mirror.
On a development machine with a populated global package cache, `powershell -ExecutionPolicy Bypass -File
.\scripts\prepare-offline-nuget.ps1` creates a temporary local feed from cached packages; restore with
`dotnet restore .\CasePlanner.slnx --source .\temp\offline-nuget`. This can still report missing packages
if the cache was not populated by a prior full restore.

The framework-dependent IIS-oriented publish profile is
server/CasePlanner.Web.Server/Properties/PublishProfiles/IisFrameworkDependent.pubxml. It requires the
approved ASP.NET Core/.NET 10 hosting runtime on the Windows server; Node.js is not required at runtime.

Production document-storage dependency:

- An IT-managed Windows file share (UNC path) or mounted filesystem path reachable from every application
  server. The ASP.NET service identity needs create/read/write permissions beneath that root; users do not
  need direct share access because downloads are served through the authorized API.
- Configure `DocumentStorage__Provider=FileSystem` and `DocumentStorage__RootPath` with that approved path.
  If `RootPath` is blank, development falls back to the local `exports` folder.
- The share must have agency-approved backup, retention, malware scanning, capacity monitoring, and access
  auditing. SQL Server stores document metadata and the file path; document bytes are not stored in SQL Server.
- The unified document platform's template source files also use this root, beneath
  `templates/documents/platform`, with every version referenced by
  `document_template_versions.storage_path`. The application service identity therefore needs the same
  create/read permissions for template versions. Retiring a template does not remove its source file; IT
  retention and backup policy applies to templates as well as generated case documents.

Client:

```powershell
cd client
npm install
npm run build
```

Server:

```powershell
cd server\CasePlanner.Web.Server
dotnet restore
dotnet build
dotnet run -- --urls http://127.0.0.1:5188
```

Then open:

- `http://127.0.0.1:5188`
- `http://127.0.0.1:5188/api/diagnostics`
- `http://127.0.0.1:5188/api/health`

SQL Server migration tooling:

```powershell
$env:CASEPLANNER_SQLSERVER_CONNECTION_STRING = "Server=localhost;Database=CasePlanner;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
dotnet run --project server\CasePlanner.DatabaseMigrator -- --sqlite data\case_planner_web.sqlite
```

Use a fresh target database. Full setup, safety notes, and the remaining cutover work are documented in
`docs/sql-server-migration.md`. Connection strings are intentionally blank in committed settings; use
environment variables, .NET user-secrets, or the deployment platform's secret store.

## Import status

- Excel import (.xlsx/.xlsm) is implemented via ClosedXML (pure local file parsing; no Excel automation/COM).
  It reads the Open, Closed, and Discovery sheets of the condemnation case list workbook and upserts
  cases by case number (or job + tract). Re-import updates rather than duplicates.
- CSV import is implemented.
- Separate SQL Server pilot CSV and Excel import endpoints are available when
  `Database__SqlServerPilotWritesEnabled=true`. The SQL Excel pilot currently imports only the Open and
  Closed case sheets. It reports the Discovery sheet as a cutover blocker instead of silently skipping it.
- `import_samples/case_import_template.csv` is included for testing.
- No Excel automation is used.

Import notes:

- You may open the CSV template in Excel if desired, then save it as CSV.
- Do not rename columns unless explicit mapping is added later.
- Blank dates are allowed.
- Use `YYYY-MM-DD` for dates when possible.
- `1900-01-01` is treated as blank.
- Deposit amount may include commas or dollar signs.

## Current guardrails

- No cloud or external API calls by the application runtime
- No email/calendar integration
- No Microsoft Word automation
- No Microsoft Excel automation
- No production database access
- Blank dates stay blank and `1900-01-01` is treated as blank
- Theme preference stays in local browser storage only

## Database status

SQLite remains the active runtime provider. A provider-neutral data project, SQL Server connection probe,
and SQLite-to-SQL Server schema/data migrator are included. The application will not silently switch
providers merely because a SQL Server connection string is present.

Case list/search and case saves now run through a shared case-catalog contract. A SQL Server pilot reader,
reconciliation endpoint, and concurrency-protected pilot writer are implemented, but normal routes still
resolve to SQLite. SQL Server deletion is implemented as an audited soft delete. Pilot writes are disabled
by default; see `docs/sql-server-migration.md`.

Deadline, checklist, discovery-tracking, case-note, hearing, witness, exhibit, trial-motion, valuation,
comparable-sale, publication-entry, activity-log, document-export, risk-analysis, risk-history, and risk-offer stores are also extracted, with SQL Server read reconciliation,
optimistic concurrency, and audited soft deletion where deletion is supported. Deadline/checklist handling
also preserves completion transitions and deadline history. Normal application and work-queue routes still
resolve to SQLite until the complete workspace cutover is ready.

The activity and document SQL Server pilots now also support gated writes, `rowversion` conflict detection,
activity edit history, document QA updates, audited document soft deletion, and centralized text-document
generation. `Database__SqlServerPilotWritesEnabled` remains `false` by default.

Normal document-export listing, content, download, QA, edited-preview persistence, and basic Case Summary /
Case Review Memo generation now use a provider-neutral generated-document service. SQL generation writes the
file through the configured central storage provider before inserting SQL metadata and removes the file if
metadata persistence fails.

The risk-analysis pilot preserves one current ledger per case and creates an immutable history snapshot for
every successful save. Current ledgers, snapshot deletion, and offer-log edits use SQL Server `rowversion`
tokens, actor-aware audit events, and non-destructive soft deletion. All 58 home-development cases reconcile
for current risk results, saved history, and offer logs. Normal risk ledger, history, offer, preview, save,
delete, and Excel-export routes now use a provider-neutral risk service. Manual risk narrative generation
remains SQLite-coupled because it also assembles valuation context through the monolithic repository.

Checklist work templates, checklist-template items, and deadline templates now also have SQL Server pilot
stores. Catalog edits use `rowversion`, global actor-aware audit events, and soft deletion; deleting a
checklist template also retires its child items in the same transaction. The migration normalizes older SQL
workflow labels to the current case-status vocabulary. The home-development catalog reconciles at 35
checklist templates and 6 deadline templates.

Normal deadline refresh, checklist refresh, candidate preview, and reviewed template-selection routes now use
a provider-neutral workflow-generation service. SQL generation reads the same case status, track, issue tags,
template catalogs, and duplicate rules, then writes through the concurrency- and audit-aware SQL work-item
stores. Candidate sets, duplicate flags, and calculated dates reconcile for all 58 development cases.

Issue-tag catalog reads and per-case assignments now use a provider-neutral store. SQL assignments add
authenticated audit events, use `rowversion`, soft-delete on removal, reject duplicates, and preserve the
existing behavior that applies or retires issue-triggered checklist tasks.

The old per-kind text templates, the standalone Custom Templates upload screen, and the Discovery Content
bulk-text editor (discovery base documents, issue-tag discovery blocks, and discovery-generation snapshots)
are retired, along with every SQLite and SQL Server code path that read or wrote them (migrations 025-026;
see `docs/sql-server-migration.md`). They're replaced by one unified document platform: templates, versions,
section/loop content, runtime inputs, and generation history are all DB-backed
(`document_templates`, `document_template_versions`, `document_template_sections`,
`document_section_overlaps`, `document_runtime_inputs`, `document_generations`) and merged natively into
uploaded `.docx` files with no third-party templating dependency. This platform is currently SQLite-only;
its SQL Server implementation (`IDocumentPlatformService`) is a deliberate not-yet-built stub pending SQL
Server sandbox access — see `docs/it-deployment-handoff.md`.

Organization-wide document defaults now also have a SQL Server singleton record. Attorney/contact/address
and leadership values retain the same document-token behavior while adding `rowversion` conflict detection,
authenticated updater attribution, and global audit events. The SQL record is initialized automatically
from the migrated SQLite `org_defaults_json` value.

SQL Server can now compose the complete current case-workspace response from the migrated case, deadline,
checklist, discovery, publication, issue-tag, note, hearing, activity, and document stores. The standard
dashboard, service queue, and upcoming-work queue also have assignment-filterable SQL pilot queries using
the same shared service-status rules as SQLite. Normal workspace, dashboard, attorney-dashboard,
service-queue, and upcoming-work routes now use a provider-neutral operational-query contract. They currently
resolve to SQLite because SQLite remains the guarded active provider.

Pilot verification routes:

- `/api/database/sqlserver-pilot/workspace/{caseId}`
- `/api/database/sqlserver-pilot/dashboard`
- `/api/database/sqlserver-pilot/service-queue`
- `/api/database/sqlserver-pilot/upcoming-work`
- `/api/database/reconciliation/workspace-dashboard/{caseId}`

The home-development `CasePlannerDev` verification reconciled all 58 case workspaces without a mismatch.

The attorney dashboard now uses a provider-neutral composer shared by SQLite and SQL Server. Its summary
cards, action queue, discovery control, momentum review, filing pipeline, trial watch, upcoming decisions,
project watch, and docket summary are available from
`/api/database/sqlserver-pilot/dashboard/attorney`. SQL Server applies the visible-case assignment set before
composition. Unfiltered and representative county, priority, holder, trial-track, matter-type, momentum, and
search filters reconcile through `/api/database/reconciliation/dashboard-attorney`.

New activity, document-generation, document-QA, discovery, deadline, checklist, and publication audit values
now use the authenticated Entra user ID and display label. Local development uses the explicit
`Local development user` label; imported historical rows are not rewritten.

Microsoft Entra authentication is scaffolded and disabled by default. Configuration requires separate API
and SPA app registrations; see `docs/microsoft-entra-setup.md`. Do not enable it for broad access until
case-assignment authorization covers all API routes. Assigned case workspaces, both dashboards, case lists,
global work queues, migrated child saves/deletes, document downloads/QA, and risk-offer records now enforce
assignment roles. Dashboard counts are computed only from assigned cases. Unclassified routes remain closed
by default, while required organization-wide catalogs are read-only for ordinary users. The current
Entra-enabled mode retains an administrator-only pilot gate pending IT security testing and final cutover.

Production resources are intentionally separate from the home development SQL Server. The environment
template and IT-owned deployment checklist are in `server/CasePlanner.Web.Server/appsettings.Production.example.json`
and `docs/it-deployment-handoff.md`; neither contains production credentials.

## Cutover administration

Use these endpoints from a restricted migration environment:

- `GET /api/database/administration-capabilities` describes which administrative operations belong to the
  application and which belong to IT/DBA.
- `GET /api/database/cutover-readiness` runs the aggregate SQLite/SQL Server reconciliation report and returns
  the remaining runtime and operational blockers.
- `POST /api/database/sqlserver-pilot/import/cases-csv` imports a multipart `file` directly into the SQL Server
  case catalog.
- `POST /api/database/sqlserver-pilot/import/cases-xlsx` imports the Open and Closed sheets directly into the
  SQL Server case catalog.

SQL Server pilot imports are disabled by default and must never be enabled for ordinary users while SQLite is
the active runtime. `Database:ActiveProvider=SqlServer` remains intentionally rejected at startup. The
migrated case, child-record, operational-query, risk, and document-composition interfaces now select their
implementation from that setting. Risk narratives have a SQL Server implementation. Template/settings
administration, import, and database maintenance were the next cutover surfaces; organization-default,
checklist, and deadline template administration are now provider-selected with SQL concurrency tokens.
Case quick actions
(next action, waiting, deferment, holder, priority, trial track, and short note) are also provider-selected and
require the case concurrency token in SQL Server. Discovery posture, pipeline handoffs, and activity history
are now provider-selected with SQL concurrency tokens and authenticated actor attribution. The canonical
publication summary is also provider-selected and concurrency protected. Child-record authorization lookups
are now provider-selected as well. Case-notes export now uses the provider-neutral workspace and shared
document storage. Normal CSV/XLSX imports now select the active provider, and the reference library is
editable through Settings while remaining file-based in the configured shared template/reference folder.
Diagnostics and database
maintenance remain SQLite-local; production SQL Server diagnostics, backup, restore, and reset belong to
IT/DBA procedures.

SQLite file backup, restore, sample-data deletion, and full reset remain valid only for the local SQLite
runtime. In a central SQL Server deployment, IT/DBA must own database backup, restore, point-in-time recovery,
retention, disaster recovery, and controlled test-data cleanup. The application must not expose a production
database reset button.

## Runtime / deployment note

- The test build runs a local ASP.NET Core web server bound to `http://127.0.0.1:5188`.
- The built React frontend is served by the ASP.NET Core backend.
- Case add/edit workflows now use in-page modal editors instead of permanently expanded forms on the page.
- Development data, backups, templates, logs, and import samples remain local to the release folder.
- Backups occur before writes.
- Generated documents use the configured filesystem provider. Development defaults to local `exports`; a
  shared deployment requires an IT-managed central path.
- Logs write to the local `logs` folder.
- Node.js/npm are required for development and build work, but not necessarily for a prebuilt self-contained runtime package.

## Current IT review summary

- Current runtime is a local prototype web app for ARDOT Legal Division.
- The runtime still launches on localhost; network hosting is intentionally deferred until identity and authorization are implemented.
- SQLite is the current source database; SQL Server is an explicit migration target with a guarded transfer utility.
- SQL Server connectivity is the only newly introduced external database connection.
- Local file import/export only.
- No Microsoft Word automation.
- No Microsoft Excel automation.
- No email/calendar integration.
- Database creation, SQL Server permissions, TLS, backup policy, and deployment hosting require IT/DBA coordination.
