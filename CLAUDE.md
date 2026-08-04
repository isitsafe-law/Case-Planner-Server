# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

ARDOT Legal Division Case Planner Web: a case/docket management app for eminent-domain (condemnation)
litigation. Currently a local, single-user ASP.NET Core + React app bound to `localhost`, mid-migration
to a centrally hosted, multi-user architecture (SQL Server + Entra auth). See `README.md` for the full,
frequently-updated feature list and migration status — it is the most current record of what's SQLite-only
vs. SQL-Server-piloted and should be treated as more current than this file for that detail.

## Commands

### Server (`server/CasePlanner.Web.Server`, targets `net10.0`)

```powershell
cd server\CasePlanner.Web.Server
dotnet restore
dotnet build
dotnet run -- --urls http://127.0.0.1:5188
```

A `.claude/launch.json` config (`caseplanner`) runs `dotnet run --no-build --project server/CasePlanner.Web.Server`
on port 5188. Once running: `http://127.0.0.1:5188`, `/api/diagnostics`, `/api/health`.

### Tests (xUnit, `server/CasePlanner.Web.Server.Tests`)

```powershell
dotnet test server\CasePlanner.Web.Server.Tests\CasePlanner.Web.Server.Tests.csproj
```

Single test:

```powershell
dotnet test server\CasePlanner.Web.Server.Tests\CasePlanner.Web.Server.Tests.csproj --filter "FullyQualifiedName~ClassName.MethodName"
```

### Client (`client/`, React 19 + TypeScript + Vite)

```powershell
cd client
npm install
npm run dev       # dev server
npm run build     # tsc -b && vite build
npm run lint      # oxlint
npm run test      # vitest run
```

Single vitest test: `npx vitest run src/ui/__tests__/SomeFile.test.tsx -t "test name"`.

### Repeatable validation

`powershell -ExecutionPolicy Bypass -File .\scripts\phase1-smoke.ps1` (add `-Restore` on a clean machine
with NuGet access, or `-WebOnly` when NuGet is unavailable and only the server build/publish path needs
checking). Builds the solution, runs the full xUnit suite, and does a build/publish smoke check.

### SQL Server migration tooling

```powershell
$env:CASEPLANNER_SQLSERVER_CONNECTION_STRING = "Server=localhost;Database=CasePlanner;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
dotnet run --project server\CasePlanner.DatabaseMigrator -- --sqlite data\case_planner_web.sqlite
```

Use a fresh target database. Details in `docs/sql-server-migration.md`.

## Architecture

### Provider-neutral dual persistence — the central pattern

Nearly every domain area (cases, deadlines, checklists, discovery, notes, hearings, witnesses, risk
analysis, document generation, etc.) follows the same shape:

- A store/service interface (e.g. `ICaseCatalogStore`, `IDeadlineStore`).
- A SQLite implementation (the active runtime store) plus a `SqlServer*` implementation (pilot-only,
  gated behind `Database:SqlServerPilotWritesEnabled`, off by default).
- One-time selection at startup in `server/CasePlanner.Web.Server/Program.cs`: DI reads
  `Database:ActiveProvider` (`appsettings.json`) and registers the matching implementation per interface.
  `Database:ActiveProvider=SqlServer` is intentionally rejected at startup — SQLite remains the guarded
  active provider even though most SQL Server pilot stores already exist and reconcile against it.
- `*ReconciliationService` classes under `Persistence/` compare SQLite vs. SQL Server output for the same
  request, used to validate the migration ahead of cutover (see the reconciliation and pilot endpoints
  under `/api/database/...`).

When changing a domain area, find its provider-neutral service/store pair under `Services/` or
`Persistence/` first rather than editing SQLite-specific code in isolation — the pilot side needs to stay
in sync even though it isn't the live path yet.

### No MVC controllers — routes are minimal APIs in Program.cs

`Controllers/WeatherForecastController.cs` is unused template scaffold. The real API surface (~300
routes) is minimal-API endpoints (`app.MapGet`/`MapPost`/...) registered directly in `Program.cs`
(~2,100 lines). To find a route, grep `Program.cs` for its path rather than looking for a controller.

### Two large, still-monolithic files

- `server/CasePlanner.Web.Server/Services/CasePlannerRepository.cs` (+ its `.DocumentPlatform.cs`
  partial) is a ~10,000-line SQLite-backed repository. Most logic not yet cut over to the
  provider-neutral pattern above still funnels through it — notably manual risk-narrative generation,
  which remains SQLite-coupled because it assembles valuation context through this repository.
- `client/src/App.tsx` is a ~12,000-line file holding almost the entire client UI (state, tabs, modals,
  reports) inside one `App()` component, plus an `api<T>()` fetch helper used throughout. Only some
  features are extracted, into `client/src/case-workspace/`, `client/src/dashboard/`, and
  `client/src/ui/` (shared primitives + `format.ts`, the canonical date/currency formatters). Prefer
  extracting new UI into those folders over adding to `App.tsx`.

### Design system

`design-system/MASTER.md` is the UI/UX source of truth (the "Docket" identity: IBM Plex Sans/Mono,
cool-neutral surfaces, one blue, Arkansas crimson used once as a brand tick). It defines the color
tokens, type/spacing scale, the canonical component list (`Button`, `DataTable`, `StatusChip`,
`FilterBar`, etc. — each meant to replace several existing ad hoc implementations), and formatting
rules (dates always via `formatDate`/`formatDateTime` from `client/src/ui/format.ts`, currency via
tabular data-font, `—` for empty values). Style new UI against it and reuse the `client/src/ui/`
components instead of one-off styles or a second date formatter.

### Auth and case access

Microsoft Entra auth is scaffolded (`Security/`, `EntraOptions`, `EntraClaims`,
`EntraUserProvisioningMiddleware`) but disabled by default (`Authentication:Entra:Enabled=false`).
`CaseAccessService`/`CaseAccessEvaluator` implement per-user case assignment; `IsUnrestricted` covers
Administrators plus any user flagged `is_manager` or with `manager_tier` Chief Counsel/Deputy Chief
Counsel — a broad rule affecting every assignment-aware endpoint (case list, workspaces, exports,
dashboards). There is no per-action role exception left in the codebase (the former Settlement
Authority workflow, which used to be Chief-Counsel-exclusive, was removed entirely — see below).

### Document generation

Generated documents (Case Summary, Case Review Memo, and the unified document platform's templates)
merge natively into `.docx` via `DocxSectionMerger`/`DocumentGenerationEngine` (DocumentFormat.OpenXml) —
no Word automation/COM, no third-party templating engine. The unified document platform
(`DocumentPlatformService`; tables `document_templates`, `document_template_versions`,
`document_template_sections`, `document_section_overlaps`, `document_runtime_inputs`,
`document_generations`) is currently SQLite-only — its SQL Server implementation
(`IDocumentPlatformService`) is a deliberate not-yet-built stub pending SQL Server sandbox access.

## Guardrails

These are product/compliance constraints, not style preferences — see README's "Current guardrails" and
"Current IT review summary" for the authoritative list. The load-bearing ones for day-to-day coding:

- No cloud or external API calls, no Microsoft Word/Excel automation, no production database access from
  this environment. Outlook/calendar integration is a real long-term goal (not yet scoped or approved for
  implementation) — don't build it opportunistically, but don't treat it as permanently off-limits either.
  Confirm scope with the user before starting any calendar-integration work.
- Blank dates stay blank; `1900-01-01` is treated as blank — don't "fix" either as a bug.
- Don't enable `Database:ActiveProvider=SqlServer` or SQL Server pilot writes for ordinary users; SQLite
  is the deliberately guarded active provider until cutover is complete.
- Don't rename or reorder CSV import columns without adding explicit mapping logic.

## Key docs

- `README.md` — full feature list and the current, detailed SQLite/SQL-Server migration status per domain area.
- `docs/sql-server-migration.md`, `docs/it-deployment-handoff.md` — migration/cutover and IT deployment details.
- `docs/microsoft-entra-setup.md` — Entra auth configuration.
- `design-system/MASTER.md` — UI/UX source of truth.
