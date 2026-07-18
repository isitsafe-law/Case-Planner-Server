SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF COL_LENGTH(N'$(Schema).discovery_tracking', N'row_version') IS NULL
    ALTER TABLE [$(Schema)].[discovery_tracking] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).discovery_tracking', N'is_deleted') IS NULL
    ALTER TABLE [$(Schema)].[discovery_tracking] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_discovery_tracking_is_deleted] DEFAULT (0);
IF COL_LENGTH(N'$(Schema).discovery_tracking', N'deleted_utc') IS NULL
    ALTER TABLE [$(Schema)].[discovery_tracking] ADD [deleted_utc] datetime2 NULL;
IF COL_LENGTH(N'$(Schema).discovery_tracking', N'deleted_by_user_id') IS NULL
    ALTER TABLE [$(Schema)].[discovery_tracking] ADD [deleted_by_user_id] uniqueidentifier NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_discovery_tracking_deleted_by_user')
    EXEC(N'ALTER TABLE [$(Schema)].[discovery_tracking] ADD CONSTRAINT [FK_discovery_tracking_deleted_by_user] FOREIGN KEY ([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users] ([id]);');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).discovery_tracking') AND name=N'IX_discovery_tracking_case_deleted')
    CREATE INDEX [IX_discovery_tracking_case_deleted] ON [$(Schema)].[discovery_tracking] ([case_id],[is_deleted]);
