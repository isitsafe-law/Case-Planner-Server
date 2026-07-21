SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Multi-user rollout Phase 3 (shared witness registry): a new global witness_persons table - one
-- identity per real person, shared across every case they're a witness in - plus a nullable
-- person_id link on the existing per-case dbo.witnesses table. Unlike Phase 1/2's SQL-Server-only
-- functional pieces (e.g. checklist_items.assigned_user_id, only meaningfully populated once
-- Entra is enabled), this feature has full dual-provider parity: SQLite gets the same-shaped
-- table (see CasePlannerRepository.cs's schema-init block) and the search/link behavior works
-- identically on both providers. witness_persons follows 031_case_opposing_attorneys.sql's
-- conventions for a brand-new table (row_version for optimistic concurrency, is_deleted/
-- deleted_utc/deleted_by_user_id for soft delete, FK to dbo.app_users for deleted_by) even though
-- this batch doesn't build a delete endpoint for it - matching the established table shape so a
-- later batch doesn't need a second migration just to add what should have been there from the
-- start. witnesses.name/contact_info are NOT removed - they remain a per-case snapshot copy,
-- populated FROM the linked person going forward but not overwritten if the person's canonical
-- record changes later. There is no live SQL Server sandbox available here to exercise this
-- against a real pilot instance - same caveat already noted for the rest of the dormant
-- multi-user foundation.

IF OBJECT_ID(N'$(Schema).witness_persons','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[witness_persons]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_witness_persons] PRIMARY KEY,
        [name] nvarchar(400) NOT NULL,
        [contact_info] nvarchar(1000) NULL,
        [created_at] datetime2 NOT NULL CONSTRAINT [DF_witness_persons_created] DEFAULT(SYSUTCDATETIME()),
        [updated_at] datetime2 NULL,
        [row_version] rowversion NOT NULL,
        [is_deleted] bit NOT NULL CONSTRAINT [DF_witness_persons_is_deleted] DEFAULT(0),
        [deleted_utc] datetime2 NULL,
        [deleted_by_user_id] uniqueidentifier NULL,
        CONSTRAINT [FK_witness_persons_deleted_by_user] FOREIGN KEY ([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users] ([id])
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).witness_persons') AND name=N'IX_witness_persons_name_deleted')
    CREATE INDEX [IX_witness_persons_name_deleted] ON [$(Schema)].[witness_persons] ([name],[is_deleted]);

IF COL_LENGTH(N'$(Schema).witnesses', N'person_id') IS NULL
    ALTER TABLE [$(Schema)].[witnesses] ADD [person_id] bigint NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_witnesses_witness_person')
    EXEC(N'ALTER TABLE [$(Schema)].[witnesses] ADD CONSTRAINT [FK_witnesses_witness_person] FOREIGN KEY ([person_id]) REFERENCES [$(Schema)].[witness_persons] ([id]);');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).witnesses') AND name=N'IX_witnesses_person')
    CREATE INDEX [IX_witnesses_person] ON [$(Schema)].[witnesses] ([person_id]);
