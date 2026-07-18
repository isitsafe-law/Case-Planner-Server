# Case Planner Server Edition — Architecture Review and Next Phase

Review date: 2026-07-16

## Executive recommendation

Use one ASP.NET Core application hosted on an IT-managed Windows server, compiled React assets, one SQL
Server database, and an approved Windows file share for templates and generated files. Do not require Node.js,
Docker, Python, Redis, RabbitMQ, Elasticsearch, Office, or a separate document-processing service in
production.

Use Microsoft Entra ID as the primary authentication model because the organization already uses Microsoft 365
identity. Keep Windows Authentication as an internal IIS fallback if Entra is not approved. Do not add local
application accounts unless IT requires an emergency break-glass path.

The first milestone should be assessment and stabilization: create a solution file, freeze a reproducible
build, record dependency/license evidence, add migration smoke tests, and produce an IIS/framework-dependent
package. The document assembly redesign should follow as a bounded subsystem, beginning with schema and
interfaces before replacing existing templates.

## Current state

### Structure

- server/CasePlanner.Web.Server: ASP.NET Core 10 web application. Program.cs contains route registration,
  dependency wiring, startup guards, and operational behavior.
- server/CasePlanner.Data: provider-neutral options, connection factory, and database probe.
- server/CasePlanner.DatabaseMigrator: SQLite-to-SQL Server migration utility and ordered SQL scripts 001–019.
- server/CasePlanner.Web.Server.Tests: xUnit tests for engines, persistence, authorization, document rules,
  and migrations.
- client: React 19, TypeScript, and Vite frontend built separately and copied into ASP.NET publish output.
- data, backups, exports, templates, logs, and import_samples: local development/runtime folders.

CasePlanner.slnx now provides one support/build entry point for all four projects. The projects remain
independently buildable through their project files.

### Database and identity

SQLite remains the active runtime provider. SQL Server is a guarded migration target selected through provider
contracts. Major case, workspace, work-item, discovery, litigation, valuation/publication, risk,
activity/document, template, settings, import, and operational-history areas have SQL implementations and
reconciliation checks. The home target CASEPLANNERDEV / CasePlannerDev is development only.

Microsoft Entra authentication is scaffolded through Microsoft.Identity.Web and the SPA uses MSAL browser
packages. Entra is disabled by default. Assignment authorization and administrator-pilot gates exist, but IT
must validate tenant/app registrations, roles, assignments, token claims, logs, and negative authorization
tests. Windows Authentication remains a viable internal IIS option; ASP.NET Core Identity would duplicate
government account administration and is not recommended as primary.

### Documents

Document composition already has IDocumentCompositionService, SQLite and SQL implementations, managed
discovery bases, issue-tag content, merge tokens, warnings, generated text snapshots, DOCX generation, QA
metadata, and central file storage. DocumentFormat.OpenXml handles DOCX/spreadsheet packages; ClosedXML handles
Excel import/export.

This is useful migration infrastructure but not the desired long-term template subsystem. Rules, template
selection, case composition, repository methods, SQL stores, and the React Documents UI still overlap.
Generated records and source files must be preserved while introducing explicit template metadata, validation,
source snapshots, and immutable versions.

### Build and deployment

Development requires .NET 10 SDK, Node.js 20+, npm, and PowerShell. Node.js is build-time only. The project
now includes a repeatable phase1-smoke.ps1 check and an IisFrameworkDependent publish profile. The IT package
remains self-contained win-x64 for fallback testing; framework-dependent IIS deployment should be primary
because IT can centrally patch the .NET runtime.

## Dependency inventory

Production/runtime: ASP.NET Core/.NET 10; Microsoft.Data.SqlClient when SQL Server is active;
Microsoft.Identity.Web when Entra is active; DocumentFormat.OpenXml for DOCX; ClosedXML for supported Excel
import/export; Microsoft.Data.Sqlite and SQLitePCLRaw only while SQLite/local migration support remains.

Build/development only: Node.js, npm, TypeScript, Vite, React build tooling, Vitest, Testing Library, jsdom,
and oxlint. MSAL browser packages are compiled into browser assets and do not require Node.js on the server.

Not present and not required: Docker, Python, Redis, RabbitMQ, Elasticsearch, Office Interop, Graph SDK, or
SharePoint SDK. Do not add any without a demonstrated requirement and license/maintenance review.

## Recommended production architecture

Primary: IIS or another IT-approved ASP.NET Core Windows host, one application, compiled frontend assets,
SQL Server, and a secured central file share. Keep secrets and environment settings outside source control.
Use built-in health checks, ASP.NET structured logging, Windows/IIS logs, and central collection.

Fallback: self-contained win-x64 publish hosted as an IT-managed Windows service or internal server process,
still using SQL Server and the central share.

Microsoft 365 is not application hosting. SharePoint, Microsoft Graph, Power Platform, and Azure are separate
services. Evaluate them later for document storage or workflow, but keep the core application independent.

## Document assembly redesign

Create boundaries using IDocumentTemplateRepository, IDocumentDataProvider, IDocumentValidator,
IDocumentRenderer, IGeneratedDocumentStore, and IDocumentAssemblyService.

Use DOCX templates with Open XML content controls or a constrained placeholder convention. Avoid Word Interop
and unattended Office. The renderer must support values, dates, currency, optional sections, repeating rows,
conditional clauses, signatures, headers/footers, page breaks, and validation warnings without embedding
entire templates in C# strings.

Workflow: select category and immutable template version; load case data; collect supplemental answers;
validate; show unresolved values; generate DOCX; store metadata, source snapshot, answers, warnings, and file
location; preserve immutable history; allow regeneration, superseding, and download.

Recommend a secured Windows file share plus SQL Server metadata first. It avoids large database binaries while
preserving auditability. Evaluate SharePoint/Graph only after the core subsystem works independently.

## Proposed schema

Add migrations for document_template_categories, document_templates, document_template_versions,
document_template_requirements, generated_documents, document_generation_answers,
document_generation_events, and document_file_records. Preserve and map existing document_exports,
custom-template metadata, generated files, and audit records before retirement.

## Phased plan

1. Assessment/stabilization: solution file, dependency/license inventory, reproducible builds, migration
   smoke tests, IIS/framework-dependent package, health/logging review, and release checklist.
2. Document foundation: schema, interfaces, template repository, requirements, validator, generated-document
   store, and one renderer.
3. First templates: correspondence, caption/certificate, one discovery document, and one motion/pleading.
4. Administration: upload, test, publish, retire, requirements review, categories, and history.
5. IT review: hosting, SQL permissions, Entra, firewall, backup/recovery, rollback, monitoring, security.
6. Optional Microsoft 365 integration only after the core works independently.

## Decisions requiring approval

- IIS versus Windows service/internal hosting.
- Entra ID versus Windows Authentication.
- Central document/reference share, permissions, retention, malware scanning, and backup.
- SQL Server version, authentication, TLS, runtime permissions, and DBA migration process.
- File share versus future SharePoint/Graph storage.
- Legal template ownership, approval authority, effective dates, retention, and first representative document.

## Risks

- SQL activation remains guarded and must not be enabled by configuration alone.
- SQLite initialization, diagnostics, maintenance, and some legacy paths remain in the monolithic repository.
- Document rules have overlapping legacy/provider-neutral paths and risk silent template divergence.
- File-share outage, permissions, locking, and backup behavior can block document generation.
- Entra testing needs real app registrations and pilot users.
- Discovery worksheet import remains incomplete in the SQL pilot.
- Ordered SQL scripts require DBA execution discipline.
- The solution is now `CasePlanner.slnx`; clean-machine restore still depends on NuGet access or a complete
  internal package mirror.

## Recommended first milestone

Approve Phase 1 only: stabilize and document the current architecture, add a solution file and reproducible
build/package checks, inventory exact dependency licenses, and add migration/health smoke tests. Do not begin
the full document rewrite or add packages until IT and the product owner approve hosting/authentication and
select the first representative legal template.
