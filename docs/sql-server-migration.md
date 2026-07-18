# SQL Server migration foundation

## Current boundary

The application runtime still uses the existing SQLite repository. SQL Server is now an explicit
migration target, but setting a SQL Server connection string does **not** switch production reads or
writes. This is deliberate: the current repository contains SQLite-specific schema upgrades, identity
queries, reset behavior, and file backup behavior that must be ported and tested before a safe cutover.

Do not bind the web server to a shared network interface yet. Authentication, authorization, per-user
case ownership, audit identity, and server-grade backup/restore are required before multi-user use.

## Added projects

- `server/CasePlanner.Data` contains provider names, provider-neutral connection creation, and a
  connection probe. It references both `Microsoft.Data.Sqlite` and `Microsoft.Data.SqlClient`.
- `server/CasePlanner.DatabaseMigrator` is a repeatable command-line utility that reads the live SQLite
  catalog, creates matching SQL Server tables and indexes, preserves integer primary keys, copies data,
  and writes a `caseplanner_migrations` audit row.
- `server/CasePlanner.DatabaseMigrator/Sql/001_multi_user_foundation.sql` adds SQL Server-only
  `app_users`, `case_assignments`, and `audit_events` tables plus `rowversion` concurrency data. The
  runtime does not consume these tables until authentication and repository cutover are implemented.

The migration utility refuses to copy into a non-empty destination table by default. Use a fresh,
dedicated database. `--allow-non-empty` only disables that safety check; it does not perform conflict
resolution and is intended for controlled recovery work.

## Create and verify a target

Prerequisites:

1. SQL Server 2019 or newer (SQL Server Developer is suitable for local development).
2. A database created by a DBA, for example `CasePlanner`.
3. A login/user with `CONNECT`, `CREATE TABLE`, `ALTER`, `SELECT`, `INSERT`, and `CREATE SCHEMA` for the
   initial migration. Runtime permissions will eventually be narrower.
4. Network/firewall/TLS configuration approved for the server environment.

Keep secrets out of committed JSON. In PowerShell, set a process-scoped environment variable:

```powershell
$env:CASEPLANNER_SQLSERVER_CONNECTION_STRING = "Server=localhost;Database=CasePlanner;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
```

For a real server certificate, omit `TrustServerCertificate=True`. It is shown only for a local
development instance with a self-signed certificate.

Create the schema without copying rows:

```powershell
dotnet run --project server\CasePlanner.DatabaseMigrator -- --sqlite data\case_planner_web.sqlite --schema-only
```

Create the schema and copy all rows into a fresh target:

```powershell
dotnet run --project server\CasePlanner.DatabaseMigrator -- --sqlite data\case_planner_web.sqlite
```

To let the web server probe the same target without storing the secret in source control:

```powershell
$env:ConnectionStrings__CasePlannerSqlServer = $env:CASEPLANNER_SQLSERVER_CONNECTION_STRING
dotnet run --project server\CasePlanner.Web.Server
```

Then request `GET /api/database/migration-target-status`. This is a connectivity check only.

## Cutover sequence

1. Introduce repository interfaces and split the large repository by bounded area (cases, workflow,
   discovery, documents, administration).
2. Port each area to provider-neutral ADO.NET or SQL Server implementations, replacing SQLite identity,
   concatenation, catalog, and DDL syntax.
3. Add optimistic concurrency (`rowversion`) to user-editable records and return HTTP 409 on stale edits.
4. Preserve the implemented authenticated actor IDs and labels as each remaining write path moves to SQL Server;
   do not retroactively assign identities to historical rows.
5. Add user/case assignment tables and enforce authorization in the API, not only in the UI.
6. Replace file-copy backups and restore endpoints with DBA-managed SQL Server backup/restore procedures.
7. Run reconciliation tests (counts, keys, nulls, monetary totals, dates, generated-document metadata),
   then perform a read-only pilot before final cutover.

The existing SQLite database remains the authoritative runtime database until those steps pass.

### Reference library provider boundary

Migration `020_reference_library.sql` adds the editable reference-library table. SQLite continues to use
the local `templates/reference` folder for the localhost package; the provider-selected SQL Server store
uses `dbo.reference_library_documents` for centralized add/edit/remove operations after cutover. Existing
reference files should be reviewed and seeded by IT as part of the controlled migration rather than copied
implicitly during application startup.

Run `powershell -ExecutionPolicy Bypass -File .\scripts\export-reference-library-sql.ps1` to create a reviewable
`temp\reference-library-seed.sql` script from the local reference folder. Review the generated SQL before
executing it against the approved database.

## Cutover administration and import gate

The server exposes an aggregate readiness report at `GET /api/database/cutover-readiness`. It runs the
implemented catalog, work-item, discovery, notes/hearings, litigation, valuation/publication,
activity/document, operational-template, discovery-template, custom-template, and organization-default
reconciliations. `ReconciledDataMatches` reports whether those checks currently agree. It is deliberately
separate from `ReadyForSqlServerActivation`, which remains false until every normal runtime route is
provider-neutral and the production operational dependencies are approved.

`GET /api/database/administration-capabilities` documents the provider-specific administration boundary.
SQLite file backup/restore and reset are local-runtime features. SQL Server backup/restore, retention,
point-in-time recovery, and disaster recovery are DBA responsibilities and are not implemented as application
HTTP operations.

Restricted pilot imports:

- `POST /api/database/sqlserver-pilot/import/cases-csv`
- `POST /api/database/sqlserver-pilot/import/cases-xlsx`

Both expect a multipart field named `file`, write directly to the configured SQL Server target, and require
`Database__SqlServerPilotWritesEnabled=true`. The Excel pilot imports the Open and Closed case sheets. If a
Discovery sheet exists, the response reports that it was not imported; that worksheet must be handled by the
separate discovery migration/reconciliation procedure before cutover.

Do not use pilot imports as a live dual-write mechanism. Run them only in a controlled migration environment,
then rerun the aggregate readiness report. The startup guard continues to reject
`Database:ActiveProvider=SqlServer` while direct SQLite surfaces remain, preventing a partial configuration
change from splitting production writes across two databases.

## Issue tags and generated-output routing

Migration `017_issue_tags_discovery_generations.sql` adds concurrency, actor, soft-deletion, and lookup
support for case issue-tag assignments and authenticated creator metadata for discovery-generation snapshots.

Normal issue-tag catalog/assignment routes now use `IIssueTagStore`. The SQL implementation rejects duplicate
active assignments, writes actor-aware audit events, generates applicable issue-triggered checklist tasks in
the same transaction, uses `rowversion` on removal, and soft-deletes assignments. Removal marks only its
still-open generated checklist tasks N/A; manual and completed work remains unchanged.

Normal discovery snapshot persistence uses `IDiscoveryGenerationStore`. SQL inserts the rendered text,
template-version list, issue-tag list, generator identity, audit event, and activity-log entry. Template
preview/rendering itself remains a separate cutover blocker.

Normal generated-document listing, content, download, QA, edited-preview persistence, and basic summary/review
generation now use `IGeneratedDocumentService`. SQL stores metadata in `document_exports` while document bytes
remain beneath the configured central document root. Template-heavy previews and custom DOCX generation still
require the remaining provider-neutral template/default composition service.

Restricted verification routes include:

- `GET /api/database/sqlserver-pilot/issue-tags`
- `GET/POST /api/database/sqlserver-pilot/cases/{caseId}/issue-tags`
- `DELETE /api/database/sqlserver-pilot/case-issue-tags/{id}?rowVersion={token}`
- `POST /api/database/sqlserver-pilot/cases/{caseId}/discovery-generations`

## Case catalog pilot

The first runtime slice now has a shared `ICaseCatalogReader` / `ICaseCatalogStore` contract. The normal
`GET /api/cases` and `POST /api/cases` routes resolve to the SQLite implementation, preserving current
behavior. SQL Server has a separate pilot implementation with:

- equivalent case searching and filtering;
- shared record mapping across both ADO.NET providers;
- insert and update support;
- concurrency-protected soft deletion (`is_deleted`, deletion timestamp, and audit event) rather than
  destructive multi-table erasure;
- SQL Server `rowversion` tokens returned as Base64;
- HTTP 409 responses when an update submits a stale token;
- an `audit_events` entry for each SQL Server pilot insert/update.

Read-only pilot endpoints:

- `GET /api/database/sqlserver-pilot/cases`
- `GET /api/database/reconciliation/cases`

SQL Server pilot writes are disabled by default. They require both a configured connection and the
process-scoped setting `Database__SqlServerPilotWritesEnabled=true`. Do not enable that flag for regular
users; the rest of the case workspace still reads and writes SQLite, so enabling independent SQL writes
would intentionally make the databases diverge.

The ordered SQL foundation scripts are applied by the migrator. `002_case_soft_delete.sql` adds the
case-lifecycle columns and index needed by the SQL Server store. Deleted SQL Server pilot cases are hidden
from catalog queries but retained for audit and possible future administrative recovery.

`003_entra_identity.sql` adds the tenant ID, object ID, last-login timestamp, and filtered unique index
used to map Microsoft Entra workforce identities. See `docs/microsoft-entra-setup.md` for app registration
and configuration.

## Deadline and checklist pilot

Deadlines and checklist items now have shared SQLite/SQL Server store contracts. Normal case and work-queue
routes still resolve to SQLite. The SQL Server pilot provides:

- matching case and global work-queue reads;
- completion timestamps that are set only on the transition to Done/Complete;
- immutable deadline due-date history;
- `rowversion` protection with HTTP 409 for stale updates;
- audited soft deletion; and
- source/target reconciliation at `GET /api/database/reconciliation/work-items`.

Read-only pilot routes are `/api/database/sqlserver-pilot/deadlines` and
`/api/database/sqlserver-pilot/checklist`. Their POST/DELETE operations require the existing
`Database__SqlServerPilotWritesEnabled=true` process setting. `005_work_item_concurrency.sql` adds the
concurrency, deletion, actor, foreign-key, and lookup-index columns. Pilot writes remain inappropriate for
normal use because generated deadlines/checklists and the rest of the workspace still run on SQLite.

## Discovery-tracking pilot

Discovery tracking now uses a shared SQLite/SQL Server store contract. The normal case-discovery and
discovery work-queue routes still resolve to SQLite. The SQL Server pilot provides equivalent reads and
writes, Base64 `rowversion` tokens, HTTP 409 responses for stale updates, audited soft deletion, and exact
source/target comparison.

Pilot and verification routes:

- `GET /api/database/sqlserver-pilot/discovery`
- `POST /api/database/sqlserver-pilot/discovery`
- `DELETE /api/database/sqlserver-pilot/discovery/{id}?rowVersion={base64-token}`
- `GET /api/database/reconciliation/discovery`

POST and DELETE require `Database__SqlServerPilotWritesEnabled=true`. The ordered
`006_discovery_tracking_concurrency.sql` script adds the concurrency, deletion, actor, foreign-key, and
case lookup index required by this store. As with the other pilot slices, do not enable SQL Server writes
for normal users before the entire related workspace is cut over atomically.

## Case-note and hearing pilot

Case notes and hearings now have shared SQLite/SQL Server store contracts. Normal case-note, hearing, and
hearing work-queue routes still resolve to SQLite. SQL Server pilot operations provide exact source/target
comparison, Base64 `rowversion` concurrency, HTTP 409 responses for stale writes, actor-aware audit events,
and non-destructive soft deletion.

Pilot and verification routes:

- `GET/POST /api/database/sqlserver-pilot/case-notes`
- `DELETE /api/database/sqlserver-pilot/case-notes/{id}?rowVersion={base64-token}`
- `GET/POST /api/database/sqlserver-pilot/hearings`
- `DELETE /api/database/sqlserver-pilot/hearings/{id}?rowVersion={base64-token}`
- `GET /api/database/reconciliation/case-workspace`

POST and DELETE require `Database__SqlServerPilotWritesEnabled=true`. The ordered
`007_notes_hearings_concurrency.sql` script adds concurrency/deletion metadata, actor foreign keys, and
case/deletion lookup indexes. The migrated legacy hearing date remains text at this stage; the index avoids
using that `nvarchar(max)` column so migration does not silently narrow or truncate existing values.

## Litigation workspace pilot

Witnesses, exhibits, and trial motions now have shared SQLite/SQL Server store contracts. Their normal case
workspace routes still resolve to SQLite. SQL Server pilot operations provide source/target reconciliation,
Base64 `rowversion` tokens, HTTP 409 responses for stale writes, actor-aware auditing, and soft deletion.

Pilot and verification routes:

- `GET/POST /api/database/sqlserver-pilot/witnesses`
- `DELETE /api/database/sqlserver-pilot/witnesses/{id}?rowVersion={base64-token}`
- `GET/POST /api/database/sqlserver-pilot/exhibits`
- `DELETE /api/database/sqlserver-pilot/exhibits/{id}?rowVersion={base64-token}`
- `GET/POST /api/database/sqlserver-pilot/trial-motions`
- `DELETE /api/database/sqlserver-pilot/trial-motions/{id}?rowVersion={base64-token}`
- `GET /api/database/reconciliation/litigation-workspace`

POST and DELETE require `Database__SqlServerPilotWritesEnabled=true`. The ordered
`008_litigation_workspace_concurrency.sql` script supplies concurrency/deletion metadata, actor foreign
keys, and case/deletion indexes for all three tables.

## Valuation and publication pilot

Valuation positions, comparable sales, and publication/service entries now have shared SQLite/SQL Server
store contracts. Normal application routes still resolve to SQLite. The SQL Server pilot provides exact
field reconciliation, Base64 `rowversion` concurrency, HTTP 409 responses for stale updates, actor-aware
auditing, and soft deletion for comparable sales and publication entries.

Valuation positions retain the one-row-per-case-and-side rule. Creating a second row for the same case and
side returns a validation error requiring a reload instead of silently overwriting another user’s current
position. Updates require the loaded row-version token.

Pilot and verification routes:

- `GET/POST /api/database/sqlserver-pilot/valuation-positions`
- `GET/POST /api/database/sqlserver-pilot/comparable-sales`
- `DELETE /api/database/sqlserver-pilot/comparable-sales/{id}?rowVersion={base64-token}`
- `GET/POST /api/database/sqlserver-pilot/publication-entries`
- `DELETE /api/database/sqlserver-pilot/publication-entries/{id}?rowVersion={base64-token}`
- `GET /api/database/reconciliation/valuation-publication`

POST and DELETE require `Database__SqlServerPilotWritesEnabled=true`. The ordered
`009_valuation_publication_concurrency.sql` script adds concurrency/deletion metadata, actor foreign keys,
and case/deletion indexes.

## Activity and document-metadata pilot

Activity logs (including edit history) and generated-document metadata now have SQL Server pilot readers and
gated writers. Normal activity, document generation, file access, and QA routes remain on SQLite. Compare the
two providers before moving any of those related routes:

- `GET /api/database/sqlserver-pilot/activity?caseId={optional-case-id}`
- `POST /api/database/sqlserver-pilot/activity/{caseId}`
- `PUT /api/database/sqlserver-pilot/activity/{id}?rowVersion={base64-token}`
- `GET /api/database/sqlserver-pilot/document-exports?caseId={optional-case-id}`
- `POST /api/database/sqlserver-pilot/document-exports/{caseId}/text`
- `POST /api/database/sqlserver-pilot/document-exports/{id}/qa`
- `DELETE /api/database/sqlserver-pilot/document-exports/{id}?rowVersion={base64-token}`
- `GET /api/database/reconciliation/activity-documents`
- `GET /api/database/document-storage-status`

The ordered `010_activity_document_audit.sql` script adds authenticated creator/editor/reviewer fields,
`rowversion` concurrency tokens, document soft-deletion metadata, foreign keys to `app_users`, and case lookup
indexes. POST, PUT, and DELETE require `Database__SqlServerPilotWritesEnabled=true`. Generated files use the
configured `FileSystem` document provider and are placed under `cases/{caseId}` beneath its root. Production IT
must set `DocumentStorage__RootPath` to an approved central UNC or mounted path. SQL Server retains metadata and
the path, not the file bytes. A failed metadata insert removes the newly written file so an invalid case or SQL
error does not leave an orphan behind.

Before production cutover, IT must copy any retained historical exports to the approved central root and update
their stored paths as a controlled migration. Files outside the configured root are intentionally not served by
the storage provider.

## Composed workspace and operational-query pilot

The migrated SQL stores are now composed into the current case-workspace response rather than being available
only as isolated table endpoints. The SQL pilot also builds the standard dashboard, service queue, and
upcoming-work queue from SQL data and applies the caller's visible-case assignment set before aggregation.

Routes:

- `GET /api/database/sqlserver-pilot/workspace/{caseId}`
- `GET /api/database/sqlserver-pilot/dashboard`
- `GET /api/database/sqlserver-pilot/service-queue`
- `GET /api/database/sqlserver-pilot/upcoming-work?type={type}&urgency={urgency}&limit={limit}`
- `GET /api/database/reconciliation/workspace-dashboard/{caseId}`

Service warning calculations are provider-neutral and shared by SQLite and SQL Server. Workspace reconciliation
checks the case identity, child-area counts, service result, and key dashboard totals. On the home-development
database, all 58 migrated case workspaces passed this reconciliation.

The normal workspace, dashboard, attorney-dashboard, service-queue, and upcoming-work routes now depend on
the shared `IOperationalWorkspaceQuery` contract. SQLite and SQL Server implementations are selected from
`Database:ActiveProvider`; the startup release gate still limits the active value to SQLite until all
remaining direct repository paths are migrated.

## Attorney-dashboard pilot

The attorney dashboard's business rules now live in a provider-neutral composer. Both SQLite and SQL Server
feed the same evaluator, including the rule that summary cards cover the full visible active docket while
content filters affect the detail sections. SQL Server filters the source case set by assignments before any
counts or rows are composed.

Routes:

- `GET /api/database/sqlserver-pilot/dashboard/attorney`
- `GET /api/database/reconciliation/dashboard-attorney`

Both accept the normal `matterType`, `project`, `county`, `priority`, `currentHolder`, `stage`, `trialTrack`,
`momentumStatus`, and `search` query parameters. Reconciliation compares all six summary cards plus ordered
case membership in action, momentum, pipeline, trial, decision, project, discovery-control, and docket
sections. Provider-neutral ordering prevents SQL Server collation differences from changing the UI.

Live home-development verification passed the unfiltered dashboard and representative values for county,
priority, holder, trial track, matter type, momentum status, and search.

## Risk-analysis and offer-log pilot

Risk analysis now has a SQL Server pilot for the current per-case ledger, save-history snapshots, and the
offer log. The calculation engine remains provider-neutral, so both databases compute interest, fee-shift
status, split scenarios, and contingency exposure from the same rules.

Pilot and verification routes:

- `GET/POST /api/database/sqlserver-pilot/risk-analysis/{caseId}`
- `POST /api/database/sqlserver-pilot/risk-analysis/{caseId}/preview`
- `DELETE /api/database/sqlserver-pilot/risk-analysis/{id}?rowVersion={base64-token}`
- `GET /api/database/sqlserver-pilot/risk-analysis/{caseId}/history`
- `GET /api/database/sqlserver-pilot/risk-analysis/{caseId}/history/{historyId}`
- `DELETE /api/database/sqlserver-pilot/risk-analysis/history/{historyId}?rowVersion={base64-token}`
- `GET /api/database/sqlserver-pilot/risk-analysis-offers?caseId={optional-case-id}`
- `POST /api/database/sqlserver-pilot/risk-analysis-offers`
- `DELETE /api/database/sqlserver-pilot/risk-analysis-offers/{id}?rowVersion={base64-token}`
- `GET /api/database/reconciliation/risk-analysis/{caseId}`

Every successful ledger save also inserts a new immutable `risk-v1` snapshot in the same SQL transaction.
Updating the current ledger requires its loaded `rowVersion`; offer-log updates and all supported deletes
have the same stale-write protection. Snapshot contents are never edited. Removing a snapshot only marks it
deleted with the authenticated actor and timestamp.

The ordered `011_risk_analysis_concurrency.sql` script adds concurrency tokens, creator/updater/deleter
identity fields, soft-deletion metadata, foreign keys to `app_users`, and case/deletion lookup indexes.
POST and DELETE remain disabled unless `Database__SqlServerPilotWritesEnabled=true`.

Home-development verification against `CasePlannerDev` reconciled all 58 cases. Controlled insert, update,
stale-update (HTTP 409), history creation, offer insert/update, and soft-delete checks also passed; temporary
verification rows were removed afterward.

Normal risk ledger, history, offer, preview, save, delete, and Excel-export routes now depend on the shared
`IRiskAnalysisService`. SQLite and SQL Server adapters preserve the existing route shapes while allowing the
future provider selection to move the entire risk slice together. Manual risk-narrative generation remains a
cutover blocker because it still obtains valuation and case context through the SQLite repository.

## Operational work-template pilot

The normal deadline/checklist refresh and reviewed template-selection routes now depend on
`IWorkflowGenerationService`. Its SQLite adapter preserves the current repository behavior. Its SQL adapter:

- composes candidates from the SQL case workspace and SQL template catalogs;
- applies stage, track, issue-tag, trigger-date, and pre-workflow gates;
- detects duplicates by stable template provenance and matching visible text;
- recalculates unlocked generated deadline dates with `rowversion` protection;
- inserts selected/generated deadlines and tasks through the audited SQL stores; and
- records a case activity when a generation batch changes work.

Candidate reconciliation at `GET /api/database/reconciliation/workflow-generation` compares ordered
candidate identities, duplicate flags, and due dates for every case. The home-development target reconciles
all 58 cases.

Restricted SQL verification routes:

- `GET /api/database/sqlserver-pilot/cases/{caseId}/work-template-candidates`
- `POST /api/database/sqlserver-pilot/cases/{caseId}/generate-deadlines`
- `POST /api/database/sqlserver-pilot/cases/{caseId}/generate-checklist`
- `POST /api/database/sqlserver-pilot/cases/{caseId}/work-template-selections`

The POST routes require `Database__SqlServerPilotWritesEnabled=true`.

Checklist templates, their child task definitions, and deadline templates now have SQL Server pilot
read/write stores. These are organization-wide configuration records rather than case-owned records, so
their audit events intentionally have a null `case_id` while retaining the authenticated actor.

Routes:

- `GET/POST /api/database/sqlserver-pilot/checklist-templates`
- `DELETE /api/database/sqlserver-pilot/checklist-templates/{id}?rowVersion={base64-token}`
- `POST /api/database/sqlserver-pilot/checklist-template-items`
- `DELETE /api/database/sqlserver-pilot/checklist-template-items/{id}?rowVersion={base64-token}`
- `GET/POST /api/database/sqlserver-pilot/deadline-templates`
- `DELETE /api/database/sqlserver-pilot/deadline-templates/{id}?rowVersion={base64-token}`
- `GET /api/database/reconciliation/work-templates`

The ordered `012_work_template_concurrency.sql` migration adds creator/updater/deleter identity, Base64
`rowversion` support, soft-deletion metadata, and lookup indexes. Deleting a checklist template retires all
of its currently active child items in the same transaction.

`013_work_template_status_normalization.sql` brings older migrated SQL values such as `Intake & Filing`,
`Discovery & Evaluation`, and `Trial Track` into the current `Pipeline`, `Active Litigation`, and
`Trial Preparation` case-status vocabulary. This repeatable normalization resolved drift found by live
reconciliation rather than hiding it in comparison code.

Home-development verification reconciled all 35 checklist templates, their child items, and all 6 deadline
templates. Controlled create, update, stale-update (HTTP 409), item deletion, parent deletion, and deadline
deletion checks passed, and all temporary rows and audit events were removed.

## Discovery content-version pilot

Managed discovery base documents and issue-tag discovery blocks now have append-only SQL Server version
stores. Unlike operational work templates, an existing discovery-content row is never overwritten or
deleted by normal application operations. Editing creates the next version under a transaction-owned SQL
application lock.

Routes:

- `GET/POST /api/database/sqlserver-pilot/discovery-templates`
- `POST /api/database/sqlserver-pilot/discovery-templates/{stableKey}/restore`
- `GET/POST /api/database/sqlserver-pilot/discovery-base/{kind}`
- `GET /api/database/sqlserver-pilot/discovery-base/{kind}/history`
- `GET /api/database/reconciliation/discovery-templates`

The SQL pilot applies the same merge-field validation as the active application. Base-document writes also
run the existing structural validator before saving, including signature/certificate checks and the
interrogatory issue-tag insertion marker.

The ordered `014_discovery_template_versioning.sql` migration adds authenticated creator IDs and
`rowversion` metadata while preserving historical creator labels. SQL `sp_getapplock` serializes version
allocation by stable key or document type. Saving a base document deactivates the previous active row and
inserts the new active version within one transaction; all historical content remains available.

Home-development verification reconciled all 10 latest issue-tag/base blocks. There were no pre-existing
managed base-document rows in either database. Controlled two-version item and base-document saves produced
versions 1 and 2, selected version 2 as current, rejected an unknown merge field, and retained both history
rows. Temporary versions and audit events were removed afterward.

## Custom document-template metadata and storage pilot

Uploaded `.txt`, `.md`, and `.docx` templates now have a SQL Server metadata catalog and a central
filesystem provider. Template bytes are stored beneath
`DocumentStorage__RootPath/templates/custom/{base-key}`. SQL Server stores the immutable version identity,
file path, format, extracted/unknown merge fields, uploader, active state, and retention state.

Routes:

- `GET /api/database/sqlserver-pilot/custom-document-templates`
- `POST /api/database/sqlserver-pilot/custom-document-templates/upload`
- `GET/POST /api/database/sqlserver-pilot/custom-document-templates/{templateKey}/content`
- `POST /api/database/sqlserver-pilot/custom-document-templates/{templateKey}/activate?rowVersion={token}`
- `DELETE /api/database/sqlserver-pilot/custom-document-templates/{templateKey}?rowVersion={token}`
- `GET /api/database/reconciliation/custom-document-templates`

The ordered `015_custom_document_templates.sql` migration creates the metadata table, one-version-per-base
constraints, a filtered one-active-version constraint, uploader/deleter foreign keys, `rowversion`, and
retirement indexes. SQL application locks serialize new-version allocation and activation by base key.

Template retirement is intentionally non-destructive. It hides the SQL metadata row and promotes the newest
remaining version when necessary, but retains the source file for legal/administrative retention. Physical
purging must be a separately authorized IT retention operation. If a metadata insert fails, the newly written
unregistered file is removed to avoid an orphan.

Home-development verification started with no custom templates in either catalog. A temporary text upload
created versions 1 and 2, extracted known merge fields, served editable content, switched activation with
`rowversion` protection, returned HTTP 409 for a stale activation, retired version 1, promoted version 2,
and confirmed both retained source files. Temporary SQL metadata, audits, and files were then removed and
reconciliation returned clean.

## Organization-defaults pilot

The organization-wide document token defaults now have a dedicated singleton SQL Server record rather than
remaining only as JSON in the SQLite `app_settings` table.

Routes:

- `GET/POST /api/database/sqlserver-pilot/org-defaults`
- `GET /api/database/reconciliation/org-defaults`

The ordered `016_organization_defaults.sql` migration creates the singleton record, restricts its primary
key to `1`, adds updater identity/display fields and `rowversion`, and initializes the columns from the
migrated `org_defaults_json` setting. Blank/missing settings initialize as explicit empty strings.

Updates require the token returned by the preceding GET. Stale saves return HTTP 409, and successful writes
record a global `OrganizationDefaultsUpdated` audit event. The normal `/api/org-defaults` route remains on
SQLite until the final provider cutover, so document generation behavior does not change during the pilot.

Home-development verification reconciled all nine organization-default fields. A temporary attorney-name
change updated the concurrency token, a stale repeat returned HTTP 409, and the original value was restored
using the new token. Temporary verification audit events were removed afterward.

## Provider-neutral document composition

Normal document composition now resolves through `IDocumentCompositionService`. SQLite and SQL Server load
their own case/template/risk context, then use the same rendering rules for merge tokens, managed discovery
blocks, renumbering, semantic-reference warnings, custom previews, and settlement-risk narratives. SQL custom
DOCX generation reads the immutable source from central template storage, writes the merged DOCX beneath the
central case-document root, and inserts the export metadata in SQL Server; a failed metadata insert removes the
new output file.

Read-only pilot routes:

- `POST /api/database/sqlserver-pilot/cases/{caseId}/document-preview/{kind}`
- `GET /api/database/sqlserver-pilot/cases/{caseId}/discovery-template-preview`
- `POST /api/database/sqlserver-pilot/cases/{caseId}/custom-document-preview/{templateKey}`
- `POST /api/database/sqlserver-pilot/cases/{caseId}/risk-analysis/narrative`

The write-gated DOCX route is:

- `POST /api/database/sqlserver-pilot/cases/{caseId}/custom-document-generate-docx/{templateKey}`

It requires `Database__SqlServerPilotWritesEnabled=true`.

The approved built-in templates under `templates/documents` remain versioned deployment assets and must be
installed read-only and identically on every application server. SQL Server stores managed discovery base
versions, issue-tag blocks, organization defaults, custom-template metadata, and custom source paths; it does
not replace the approved built-in template asset folder.

Home-development verification against `CASEPLANNERDEV / CasePlannerDev` rendered case 40 through both the
normal SQLite and SQL pilot paths. Interrogatory text, missing-token results, warnings, and template provenance
matched exactly (6,461 rendered characters). The risk narrative also matched exactly (1,764 characters).

## Provider-neutral template administration

Normal discovery-template/base, custom-template, organization-default, checklist-template/item, and
deadline-template routes now resolve through provider-selected administration stores. SQL Server edits and
retirements keep their existing `rowversion` checks and return HTTP 409 for stale writes. The client sends the
version token for custom-template activation/retirement and checklist-template/item deletion; edited models
carry their token in the request body.

When SQL Server has no managed discovery base version, the normal administration store returns the approved
deployed template file as version 0, matching existing SQLite behavior. The lower-level SQL pilot endpoint
continues to return only managed SQL rows so migration verification can distinguish database content from the
deployment fallback.

Read-only home verification matched 10 discovery template items, 35 checklist templates and their child-item
total, 6 deadline templates, all organization-default fields, and the empty custom-template catalog. Every SQL
checklist/deadline row returned a concurrency token.

## Provider-neutral case quick actions

The high-frequency case-header actions now resolve through `ICaseQuickActionService`:

- next action and review date
- waiting condition and clear
- deferment, clear, and bulk deferment
- current holder
- priority
- trial-track flag
- short posture note

SQL updates require the case `rowversion`, update only the intended columns, return the replacement token, and
write an authenticated audit event. Actions that historically created an activity entry continue to do so
through the SQL activity store. The client supplies the token from its case model and immediately replaces its
local token after a successful response, avoiding a false conflict on the next dashboard action.

The controlled pilot route `POST /api/database/sqlserver-pilot/cases/{id}/priority` is write-gated by
`Database__SqlServerPilotWritesEnabled`. Home verification changed case 40 from Normal to High, confirmed a
stale repeat returned HTTP 409, then restored Normal using the returned token.

## Provider-neutral operational history

Discovery posture, pipeline handoff, and activity routes now resolve through provider-selected stores.
Migration `018_operational_history_concurrency.sql` adds SQL Server `rowversion` columns, actor attribution,
foreign keys to `app_users`, a unique per-case discovery-posture index, and a pipeline-handoff lookup index.

SQL discovery-posture and activity edits require their current concurrency token and return HTTP 409 for a
stale write. Pipeline-handoff creation requires the current case token because the operation also changes the
case holder, stage, handoff date, and next-review date; the response returns the replacement case token.
Authenticated SQL writes create audit events, and newly created handoffs retain the creator ID and label.

The normal activity API is ready to accept `rowVersion` for edits. The current web interface presents Recent
Activity as a read-only audit trail, so this token is primarily for API compatibility and future administrative
editing rather than a currently exposed casual edit workflow.

Home-development verification against `CASEPLANNERDEV / CasePlannerDev` matched case 40's discovery posture,
0/0 pipeline handoffs, and 3/3 activity rows. A controlled stale discovery-posture update returned HTTP 409,
and global activity/document reconciliation remained exact at 68 activities and 6 generated-document rows.

## Provider-neutral publication summary

The canonical `case_publications` record used by the Status tab and service calculations now resolves through
`IPublicationSummaryStore`. This is distinct from the older multi-row publication-service entry catalog,
which was already provider-neutral. Normal `GET` and `PUT /api/cases/{id}/publication` routes no longer call
the SQLite repository directly.

Migration `019_publication_summary_concurrency.sql` adds a SQL Server `rowversion`, authenticated updater ID,
foreign key to `app_users`, and a unique case index. Existing summaries retain their imported updater label.
SQL updates require the current token, return its replacement, write an audit event, and preserve the existing
`PublicationChanged` activity entry. Date ordering and missing-publication-name override validation remain
shared with the SQLite behavior.

Verification endpoints:

- `GET /api/database/sqlserver-pilot/publication-summaries?caseId={caseId}`
- `PUT /api/database/sqlserver-pilot/cases/{caseId}/publication` (write-gated)
- `GET /api/database/reconciliation/publication-summaries`

Home-development verification matched 1/1 canonical summaries. A controlled case-40 update returned HTTP 409
when repeated with a stale token; the original publication values and imported metadata were then restored.

## Provider-neutral child authorization lookups

Delete routes for case notes, hearings, checklist items, deadlines, comparable sales, witnesses, exhibits, and
trial motions now resolve the owning case through ICaseChildLookupStore. SQLite delegates to the existing
repository lookup; SQL Server uses a whitelist of migrated child tables and filters is_deleted=0 before the
assignment check. This removes authorization's dependency on SQLite for those child records without changing
the existing route shapes or soft-delete behavior.

Case-notes export also now resolves through the provider-neutral workspace query and writes through the shared
document-storage abstraction. The existing text layout and filename convention are preserved, and the route
performs the same assignment-aware read check before writing the export.

Normal CSV and XLSX imports now resolve through ICaseImportService. SQLite keeps the existing local importer;
SQL Server uses the gated case-catalog importer. The SQL Excel importer intentionally reports the Discovery
worksheet as detected-but-not-imported until that worksheet has its own reviewed SQL mapping.

SQLite's IReferenceLibraryStore still reads and edits plain-text documents plus `.reference-library.json`
metadata in the configured local reference folder. After SQL cutover, the provider-selected
SqlServerReferenceLibraryStore uses `dbo.reference_library_documents`; run migration `020_reference_library.sql`
and the reviewed seed script before enabling centralized edits. A central document share may still be used for
template binaries, but reference-library metadata and text are no longer required to be shared filesystem state.
### Unified document metadata (migration 021)

Run `021_unified_document_template_metadata.sql` after migration 020. It is additive and safe to re-run:

- Adds category, description, visibility, default output name, and recommended case type to existing custom templates.
- Creates normalized `document_tags` plus template/discovery-item link tables.
- Existing rows remain intact and default to category `Other` and visibility `Personal` until an administrator edits them.

The application can continue to read older rows during a staged rollout. After the migration, IT can populate shared
tags and permissions centrally; no client machine should write directly to the server template folder.

### Case lifecycle dates (migration 022)

Run `022_case_lifecycle_dates.sql` after migration 021. It adds nullable `date_opened` plus indexes for opened and
closed-date reporting. Existing `created_at` values are retained as audit timestamps; the application does not
silently copy them into `date_opened`.

### Document platform (migration 023)

Run `023_document_platform.sql` after migration 022. This is the schema for the unified document-generation
rebuild (case Documents tab + Settings Document Templates + Interrogatories, unified into one platform). It is
additive and safe to re-run.

**New tables:**

- `document_templates` — one row per template, including the 5 former built-in kinds (`is_builtin = 1`, not
  user-deletable) and every uploaded custom template. Replaces `custom_document_templates` as the long-term
  template catalog.
- `document_template_versions` — real immutable versioning for every template, including built-ins, which had
  none before. One active version per template is enforced by a filtered unique index, the same pattern
  `custom_document_templates` already uses.
- `document_runtime_inputs` — per-version declaration of fields prompted for at generation time rather than
  resolved from the case (e.g. opposing counsel name, hearing date).
- `document_template_sections` — one row per named `{{#Section}}` block in a template version: a stable key,
  a human-readable label and description for the generation checklist, and the issue tag (if any) that defaults
  it checked.
- `document_section_overlaps` — a declarative "these two sections may say similar things" pairing, authored by
  whoever writes template content, surfaced as a warning in the generation checklist rather than automatic
  text-similarity matching. `section_a_id` must be less than `section_b_id`; the check constraint enforces this
  so a reversed duplicate pair is rejected rather than silently accepted.
- `document_generations` — one row per generated document, replacing both `document_exports` and
  `discovery_generations`. Carries a **real foreign key** to the exact template version used (the previous
  tables only stored a free-text version string) plus `sections_included_json`, the exact set of sections the
  attorney had checked at generation time — independent of whatever the case's tags are by the time anyone
  looks at the history later.

**Also in this migration:** a unique index on `issue_tags.name`. That table predates this cutover and never had
a real uniqueness guarantee — the existing seed logic only checked "not exists" before inserting the fixed
22-tag catalog. The new `POST /api/issue-tags` create-tag endpoint (the fixed-vocabulary limitation called out
in the Phase 1 audit) needs an actual database constraint, since two concurrent creates on a multi-user server
could otherwise both pass an application-level check and insert the same name twice.

**Not included in this migration:** copying data out of `custom_document_templates`, `discovery_base_versions`,
`discovery_template_items`, `document_exports`, or `discovery_generations` into the new tables. That is a
separate, deliberately later step once the new schema has been live and reviewed — see the Migration Plan in
docs/document-system-audit-and-plan for what carries over automatically versus what has to be re-authored by
hand in Word.

**Deployment note for IT:** unlike earlier pilot-stage migrations in this document, the document platform is the
intended production target going forward, not a parallel path kept in sync with SQLite for comparison. SQLite
remains the day-to-day development database only because the development environment does not yet have sandbox
access to a SQL Server instance on the agency network. When provisioning the real instance:

1. Run migrations `001` through `026` in order against a fresh, dedicated database (the migrator tool refuses to
   write into a non-empty destination by default - see "Create and verify a target" above).
2. Confirm `app_users` is populated (or your identity-provider integration is wired up) before enabling any
   `created_by_user_id`/`generated_by_user_id` columns to matter - they're nullable, so the schema itself doesn't
   require it, but audit-trail completeness does.
3. No client machine should write template `.docx` files directly to a shared folder outside the app - the
   `document_template_versions.storage_path` convention expects the application's own file-storage layer
   (`IDocumentStorage`/`ITemplateFileStorage`) to own that path, the same convention already in place for
   generated document exports.
4. This migration creates schema only; it does not touch existing SQLite data, and the runtime does not read
   from these tables until the application code that consumes them is deployed (build-plan steps 4-5).

### Issue tag retirement (migration 024)

Run `024_issue_tag_retirement.sql` after migration 023. Adds `issue_tags.is_deleted` for the new Settings → Issue
Tags admin screen's create/rename/retire actions (build-plan step 5) - retiring a tag is a soft-delete only, since
case history and document-template sections may still reference it by id/name. This migration also **replaces**
the unconditional `UX_issue_tags_name` unique index migration 023 added with a filtered `UX_issue_tags_name_active`
scoped to non-retired rows, so a retired tag's name can be reused - the unconditional version would otherwise
block that. Safe to re-run.

### Legacy document pipeline retirement (migration 025)

Run `025_retire_legacy_document_pipeline.sql` after migration 024. Build-plan step 7 (cleanup): the unified
document platform (migration 023) fully replaced three legacy systems - the fixed 5 built-in document kinds, the
old "Custom Templates" upload screen, and the old "Discovery Content" bulk-text admin screen - so this drops their
now-dead schema:

- `document_tags`, `document_template_tag_links`, `discovery_item_tag_links` - added in migration 021, confirmed by
  the Phase 1 audit to be referenced by zero C# code even before this retirement.
- `custom_document_templates` - superseded by `document_templates`/`document_template_versions`.

**Not dropped here**: `discovery_template_items`, `discovery_base_versions`. Their C# consumers are retired in this
same step, but unlike the tables above, these were never created by an explicit numbered migration - their SQL
Server schema is generated at cutover time by introspecting the live SQLite schema. Removing them from
`CasePlannerRepository.SchemaSql` is what stops them from being introspected into a fresh cutover; there is nothing
to drop here since no SQL Server instance has been stood up yet to run this migration against. If you cut over
before applying this retirement, those tables will still be created and can be dropped manually once the
application no longer references them. (`discovery_generations` was in this same category originally but is
retired explicitly in migration 026 below, once its data was confirmed empty everywhere.)

### Discovery generations retirement (migration 026)

Run `026_retire_discovery_generations.sql` after migration 025. `discovery_generations` stored raw rendered-text
snapshots from the old Discovery Content bulk editor (migration 025's retirement) - confirmed zero rows in the
local SQLite database, and nothing writes to it going forward (unlike `document_exports`, which stays live for
Case Summary/Review). `CasePlannerRepository.InitializeAsync` drops the SQLite copy directly (`DROP TABLE IF
EXISTS`) rather than waiting for a fresh database; this migration does the equivalent on SQL Server in case a
cutover already introspected the table before this migration runs.
