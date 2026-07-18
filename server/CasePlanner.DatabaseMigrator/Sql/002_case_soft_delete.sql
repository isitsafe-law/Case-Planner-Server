IF COL_LENGTH(N'$(Schema).cases', N'is_deleted') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_cases_is_deleted] DEFAULT (0);

IF COL_LENGTH(N'$(Schema).cases', N'deleted_utc') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [deleted_utc] datetime2 NULL;

IF COL_LENGTH(N'$(Schema).cases', N'deleted_by_user_id') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [deleted_by_user_id] uniqueidentifier NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_cases_deleted_by_user')
    ALTER TABLE [$(Schema)].[cases] ADD CONSTRAINT [FK_cases_deleted_by_user]
        FOREIGN KEY ([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users] ([id]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'$(Schema).cases') AND name = N'IX_cases_is_deleted_id')
    CREATE INDEX [IX_cases_is_deleted_id] ON [$(Schema)].[cases] ([is_deleted], [id]);
