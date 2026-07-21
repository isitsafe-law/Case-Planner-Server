SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Multi-user rollout Item 1: case.opposing_counsel was a single free-text string on cases with
-- zero document-generation coupling anywhere (pure case-record display). Converted to a
-- one-to-many child table, following this app's standard per-case-list shape (row_version for
-- optimistic concurrency, is_deleted/deleted_utc/deleted_by_user_id for soft delete, matching
-- witnesses/exhibits/trial_motions - see 008_litigation_workspace_concurrency.sql). Unlike those,
-- this table did not previously exist on SQL Server at all, so (following 027_service_log.sql's
-- precedent for a brand-new table) it is created here in full rather than as an ALTER. The old
-- opposing_counsel column on dbo.cases is left in place - a one-time SQLite-side migration copies
-- any existing non-blank value into a first row here; nothing drops or renames it.

IF OBJECT_ID(N'$(Schema).case_opposing_attorneys','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[case_opposing_attorneys]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_case_opposing_attorneys] PRIMARY KEY,
        [case_id] bigint NOT NULL,
        [name] nvarchar(400) NOT NULL,
        [sort_order] int NOT NULL CONSTRAINT [DF_case_opposing_attorneys_sort_order] DEFAULT(0),
        [created_at] datetime2 NOT NULL CONSTRAINT [DF_case_opposing_attorneys_created] DEFAULT(SYSUTCDATETIME()),
        [updated_at] datetime2 NULL,
        [row_version] rowversion NOT NULL,
        [is_deleted] bit NOT NULL CONSTRAINT [DF_case_opposing_attorneys_is_deleted] DEFAULT(0),
        [deleted_utc] datetime2 NULL,
        [deleted_by_user_id] uniqueidentifier NULL,
        CONSTRAINT [FK_case_opposing_attorneys_cases] FOREIGN KEY ([case_id]) REFERENCES [$(Schema)].[cases] ([id]),
        CONSTRAINT [FK_case_opposing_attorneys_deleted_by_user] FOREIGN KEY ([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users] ([id])
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).case_opposing_attorneys') AND name=N'IX_case_opposing_attorneys_case_deleted')
    CREATE INDEX [IX_case_opposing_attorneys_case_deleted] ON [$(Schema)].[case_opposing_attorneys] ([case_id],[is_deleted]);
