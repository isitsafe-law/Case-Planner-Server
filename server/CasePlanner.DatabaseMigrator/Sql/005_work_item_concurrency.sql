SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF COL_LENGTH(N'$(Schema).deadlines', N'row_version') IS NULL
    ALTER TABLE [$(Schema)].[deadlines] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).deadlines', N'is_deleted') IS NULL
    ALTER TABLE [$(Schema)].[deadlines] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_deadlines_is_deleted] DEFAULT (0);
IF COL_LENGTH(N'$(Schema).deadlines', N'deleted_utc') IS NULL
    ALTER TABLE [$(Schema)].[deadlines] ADD [deleted_utc] datetime2 NULL;
IF COL_LENGTH(N'$(Schema).deadlines', N'deleted_by_user_id') IS NULL
    ALTER TABLE [$(Schema)].[deadlines] ADD [deleted_by_user_id] uniqueidentifier NULL;

IF COL_LENGTH(N'$(Schema).checklist_items', N'row_version') IS NULL
    ALTER TABLE [$(Schema)].[checklist_items] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).checklist_items', N'is_deleted') IS NULL
    ALTER TABLE [$(Schema)].[checklist_items] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_checklist_items_is_deleted] DEFAULT (0);
IF COL_LENGTH(N'$(Schema).checklist_items', N'deleted_utc') IS NULL
    ALTER TABLE [$(Schema)].[checklist_items] ADD [deleted_utc] datetime2 NULL;
IF COL_LENGTH(N'$(Schema).checklist_items', N'deleted_by_user_id') IS NULL
    ALTER TABLE [$(Schema)].[checklist_items] ADD [deleted_by_user_id] uniqueidentifier NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_deadlines_deleted_by_user')
    EXEC(N'ALTER TABLE [$(Schema)].[deadlines] ADD CONSTRAINT [FK_deadlines_deleted_by_user] FOREIGN KEY ([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users] ([id]);');
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_checklist_items_deleted_by_user')
    EXEC(N'ALTER TABLE [$(Schema)].[checklist_items] ADD CONSTRAINT [FK_checklist_items_deleted_by_user] FOREIGN KEY ([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users] ([id]);');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).deadlines') AND name=N'IX_deadlines_case_deleted')
    CREATE INDEX [IX_deadlines_case_deleted] ON [$(Schema)].[deadlines] ([case_id],[is_deleted]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).checklist_items') AND name=N'IX_checklist_case_deleted')
    CREATE INDEX [IX_checklist_case_deleted] ON [$(Schema)].[checklist_items] ([case_id],[is_deleted]);
