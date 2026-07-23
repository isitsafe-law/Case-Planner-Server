# IT deployment handoff

This document separates home development resources from an eventual agency production deployment.
`DESKTOP-7N5464F\CASEPLANNERDEV`, its data, local certificates, and local Windows identity are development
resources only and must not be copied into production configuration.

## Production resources owned by IT

- Supported central SQL Server instance and a dedicated Case Planner database.
- Separate DBA migration credential and least-privileged application runtime credential/service identity.
- Two single-tenant Microsoft Entra app registrations described in `microsoft-entra-setup.md`.
- Approved pilot user and administrator groups assigned to the Entra enterprise applications.
- HTTPS DNS name, server certificate, reverse proxy/IIS or approved ASP.NET hosting service.
- SQL backup, point-in-time recovery, retention, monitoring, patching, and disaster-recovery procedures.
- Central application logs with access controls appropriate for legal case information.
- An approved central document share or mounted filesystem with backup, retention, malware scanning, capacity
  monitoring, and access auditing. Grant the application service identity create/read/write access; do not grant
  end users direct share access unless separately required and approved.
- The same central root stores the unified document platform's template source files under
  `templates/documents/platform` - every built-in and uploaded template's versioned `.docx` source, referenced by
  `document_template_versions.storage_path`. Template retirement in the application does not physically purge
  these files. (The older `templates/documents/custom` subtree from the retired standalone Custom Templates
  screen is no longer written to; see "Unified document platform" below.)

## Configuration inputs

Use `server/CasePlanner.Web.Server/appsettings.Production.example.json` as a field checklist, not as a
deployable production file. Supply the SQL connection through the hosting secret store or environment
variable `ConnectionStrings__CasePlannerSqlServer`; never commit a production credential.

IT must provide:

1. Production HTTPS URL and allowed host name.
2. SQL Server name, database name, encryption requirements, and runtime authentication method.
3. Directory tenant ID, API application ID, and SPA application ID.
4. Confirmation of `CasePlanner.Access`, `CasePlanner.User`, and `CasePlanner.Admin` configuration.
5. Pilot group membership and the initial application administrators.
6. Central document root supplied as `DocumentStorage__RootPath` (for example, an approved UNC path) and the
   service identity that will access it.
7. The UNC path convention for the condemnation case-file share (organized by Job/Tract) that the new
   "Open Case Folder" button launches Explorer against. Each case's `Case Folder Path` field is entered
   per case today; every user's own machine (not the application server) needs read access to that share
   for the button to work, since Explorer opens locally under the signed-in user's own session.
8. A decision on Microsoft Graph-based authorization (see "Pending Microsoft Graph decision" below) before
   that work can begin: the Entra Security Group Object IDs for Administrator/Manager equivalence,
   confirmation that `GroupMember.Read.All` (or equivalent) is requested and admin-consented on the app
   registration, and whether Graph group membership fully replaces the existing App-Role-claims checks or
   coexists as a fallback.

### Pending Microsoft Graph decision

Authorization today is entirely token-claims based (App Role claims baked into the JWT, checked via
`ClaimsPrincipal.IsInRole`/`FindAll("roles")`) - there are no live Microsoft Graph calls anywhere in the
code yet. Moving to live Graph group-membership queries is a real architectural change, not a small
addition, and is blocked until IT supplies the three items in input 8 above. No code has been written for
this; do not assume Graph integration is in progress.

## Deployment sequence

1. Restore or copy an approved SQLite cutover snapshot into a controlled migration workspace.
2. Back up the empty production database and run the database migrator with a DBA-controlled credential.
3. Reconcile every source/target table and retain the migration audit output. Run
   `GET /api/database/cutover-readiness` and retain its JSON with the cutover evidence. A matching
   reconciliation report is necessary but does not override any listed runtime blocker.
4. Deploy the server and built React assets with Entra still disabled and network access restricted to IT.
5. Verify SQL connectivity, TLS, logging, health monitoring, database backup/restore, and central document-share
   read/write/backup recovery. Check `/api/database/document-storage-status` from the deployed server.
6. Configure Entra identifiers and enable authentication with `AdministratorPilotOnly=true` for the
   administrator pilot group only.
7. Provision users, assign pilot cases, and verify positive and negative authorization tests.
8. Keep the source system read-only during final reconciliation and rollback window.
9. Run assigned/unassigned and read-only/write-role tests against dashboards, exports, child records, and
   cross-case queues; enable broader access only after those pass and all repository areas use SQL Server.

SQL Server database administration remains outside the application. IT/DBA must provide and test backup,
restore, point-in-time recovery, retention, disaster recovery, and controlled non-production data cleanup.
The SQLite backup/restore/reset endpoints are not production SQL Server procedures and must not be exposed as
such. `GET /api/database/administration-capabilities` provides a machine-readable statement of this boundary.

## Current release gate

The code is not yet approved for shared production use. SQLite remains the active runtime provider, and
case-assignment authorization is fail-closed and implemented across dashboards, queues, case routes, exports,
and current child-record routes, but still requires authenticated IT security testing. Case catalog, deadline,
checklist, discovery-tracking, case-note, hearing, witness, exhibit,
trial-motion, valuation, comparable-sale, publication-entry, activity, document, risk-analysis, risk-history,
risk-offer, checklist-template, checklist-template-item, and deadline-template SQL Server pilot capabilities are deliberately
disabled for normal writes by default to prevent an accidental partial deployment.

The SQL pilot can now compose full case workspaces and the standard dashboard/service/upcoming-work queries,
with case-assignment filtering before aggregation. All 58 home-development workspaces reconciled successfully.
The attorney dashboard is also composed from SQL through shared provider-neutral rules and has passed
unfiltered and representative filtered reconciliation. Risk calculation and storage now reconcile for all
58 development cases, including immutable save-history snapshots and offer logs. Organization settings,
templates, authenticated end-to-end security testing, remaining direct-SQLite normal routes, the Discovery
worksheet portion of Excel import, SQL Server administration runbooks, and the final provider switch remain
release gates. SQL case-catalog CSV and Open/Closed-sheet Excel pilot imports are available only behind the
existing pilot-write flag for controlled migration testing.

The normal case workspace, operational dashboards and queues, migrated case and child-record stores, and risk
ledger/history/offers now have configuration-selected SQLite and SQL Server implementations. Issue-tag
assignments, generated-document metadata/content/QA, and basic summary/review generation are provider-selected
as well. Runtime SQL activation is still blocked until risk narrative, imports, and administrative operations
no longer require the SQLite repository. Deadline/checklist refresh, candidate review, and selected template
generation are now provider-selected and reconcile across all 58 development cases. IT must validate that
failed SQL metadata writes do not leave files behind and that the central share supports simultaneous
application-server access.

Operational checklist/deadline template catalogs now reconcile and have concurrency-protected SQL pilot
writes. Imports, destructive database-management operations, and the final provider selection remain separate
release gates.

### Unified document platform

Build-plan steps 1-7 (see the audit-and-rebuild-plan artifact and `docs/sql-server-migration.md` migrations
023-026) replaced three previously-separate systems - the fixed 5 built-in document kinds, the standalone
Custom Templates upload screen, and the Discovery Content bulk-text editor - with one platform: templates,
versions, section/loop content, runtime inputs, and generation history are all DB-backed
(`document_templates`, `document_template_versions`, `document_template_sections`,
`document_section_overlaps`, `document_runtime_inputs`, `document_generations`) and merged natively into
uploaded `.docx` files with no third-party templating dependency. The old `custom_document_templates`,
`discovery_template_items`, `discovery_base_versions`, and `discovery_generations` tables and every C# code
path reading or writing them are retired; the tag-linking schema added for a feature that was never built
(migration 021) is dropped too (migration 025).

**This is currently SQLite-only.** `IDocumentPlatformService`'s SQL Server implementation is a deliberate
`NotSupportedException` stub - not an oversight. There has been no SQL Server sandbox available to develop or
verify a real ADO.NET implementation against, and shipping one unverified risks exactly the kind of
silently-wrong behavior this platform rebuild exists to avoid. Building and reconciling it is a distinct,
still-open release gate: once IT provides sandbox access, the implementation should follow the same
provider-selected pattern (and reconciliation-service pattern) used for every other table in this list, and
its central document-storage validation applies to `templates/documents/platform` the same way it does to
generated documents.

Organization defaults now have a concurrency-protected SQL singleton initialized from the cutover data.
Before production pilot access, business owners should verify the attorney identity, bar number, phone,
email, mailing address, division head, ROW section head, and Chief Legal Counsel values because these fields
flow into generated legal documents.
