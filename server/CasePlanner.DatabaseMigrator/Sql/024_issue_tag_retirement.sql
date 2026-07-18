SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Build-plan step 5 (unified Settings UI): issue_tags gets a real admin screen (create/rename/
-- retire), so "retire" needs a real column. Soft-delete only - case history (case_issue_tags)
-- and document_template_sections may still reference a retired tag by id/name, and neither
-- should end up with a dangling or silently-wrong reference. Matches the SQLite shape added in
-- CasePlannerRepository.EnsureSchemaUpgradesAsync exactly.
IF COL_LENGTH(N'$(Schema).issue_tags', N'is_deleted') IS NULL
    ALTER TABLE [$(Schema)].[issue_tags] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_issue_tags_deleted] DEFAULT(0);

-- 023_document_platform.sql added an unconditional unique index on name (UX_issue_tags_name) to
-- close a concurrent-create race. That index would now also block reusing a retired tag's name,
-- which is a real thing someone might reasonably want to do - replace it with a filtered version
-- scoped to non-retired rows.
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).issue_tags') AND name=N'UX_issue_tags_name')
    DROP INDEX [UX_issue_tags_name] ON [$(Schema)].[issue_tags];

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).issue_tags') AND name=N'UX_issue_tags_name_active')
    CREATE UNIQUE INDEX [UX_issue_tags_name_active] ON [$(Schema)].[issue_tags]([name]) WHERE [is_deleted]=0;
