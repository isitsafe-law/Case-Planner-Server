SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Build-plan step 7 follow-up: discovery_generations stored raw rendered-text snapshots from the
-- old Discovery Content bulk editor, fully retired in migration 025's companion C# cleanup.
-- Unlike custom_document_templates (dropped in 025), this table was never created by an explicit
-- numbered migration - its SQL Server schema would otherwise be generated at cutover time by
-- introspecting the live SQLite schema (see docs/sql-server-migration.md). Confirmed zero real
-- rows in the local SQLite database and nothing writes to it going forward (the SQLite copy is
-- dropped directly in CasePlannerRepository.InitializeAsync), so this drops the SQL Server side
-- too in case a cutover has already introspected it before this migration runs.

IF OBJECT_ID(N'$(Schema).discovery_generations','U') IS NOT NULL
    DROP TABLE [$(Schema)].[discovery_generations];
