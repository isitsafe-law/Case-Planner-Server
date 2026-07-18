# Case Planner IT Review Build

This package is for IT/security/database review and controlled testing. It is not approved for shared
production use. It contains no production credentials and no development SQLite database.

## Included

- Self-contained Windows x64 ASP.NET Core server and built React client
- SQL Server migration scripts under sql/
- Production configuration checklist under config/
- IT deployment, Entra, and SQL migration documentation under docs/
- The unified document platform's built-in `.docx` templates (Interrogatories, Requests for
  Admission, Judgment, Settlement Justification) and import samples

## Local smoke test

Run CasePlanner.Web.Server.exe from this folder. The build starts in guarded SQLite mode and binds to the
configured local URL. The local data folder is intentionally empty; initialize it only for a disposable test.

## IT review requirements

Before any SQL Server or Entra test:

1. Read docs/it-deployment-handoff.md, docs/microsoft-entra-setup.md, and docs/sql-server-migration.md.
2. Supply ConnectionStrings__CasePlannerSqlServer through the deployment secret store.
3. Keep Database__ActiveProvider=Sqlite until IT completes reconciliation and approves the remaining gates.
4. Do not expose SQLite backup/reset endpoints in a production deployment.
5. Provide an approved central document/reference share; the editable Reference Library writes text files and
   .reference-library.json metadata there.

The current release gates are diagnostics/provider status, IT-owned SQL backup/restore/recovery procedures,
Entra authorization testing, Discovery worksheet import, the unified document platform's SQL Server
implementation (currently a deliberate not-yet-built stub - see "Unified document platform" in
docs/it-deployment-handoff.md), and final provider activation.
