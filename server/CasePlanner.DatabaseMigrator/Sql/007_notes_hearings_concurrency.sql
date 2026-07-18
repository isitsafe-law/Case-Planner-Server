SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF COL_LENGTH(N'$(Schema).case_notes', N'row_version') IS NULL
    ALTER TABLE [$(Schema)].[case_notes] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).case_notes', N'is_deleted') IS NULL
    ALTER TABLE [$(Schema)].[case_notes] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_case_notes_is_deleted] DEFAULT (0);
IF COL_LENGTH(N'$(Schema).case_notes', N'deleted_utc') IS NULL
    ALTER TABLE [$(Schema)].[case_notes] ADD [deleted_utc] datetime2 NULL;
IF COL_LENGTH(N'$(Schema).case_notes', N'deleted_by_user_id') IS NULL
    ALTER TABLE [$(Schema)].[case_notes] ADD [deleted_by_user_id] uniqueidentifier NULL;

IF COL_LENGTH(N'$(Schema).hearings', N'row_version') IS NULL
    ALTER TABLE [$(Schema)].[hearings] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).hearings', N'is_deleted') IS NULL
    ALTER TABLE [$(Schema)].[hearings] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_hearings_is_deleted] DEFAULT (0);
IF COL_LENGTH(N'$(Schema).hearings', N'deleted_utc') IS NULL
    ALTER TABLE [$(Schema)].[hearings] ADD [deleted_utc] datetime2 NULL;
IF COL_LENGTH(N'$(Schema).hearings', N'deleted_by_user_id') IS NULL
    ALTER TABLE [$(Schema)].[hearings] ADD [deleted_by_user_id] uniqueidentifier NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_case_notes_deleted_by_user')
    EXEC(N'ALTER TABLE [$(Schema)].[case_notes] ADD CONSTRAINT [FK_case_notes_deleted_by_user] FOREIGN KEY ([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users] ([id]);');
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_hearings_deleted_by_user')
    EXEC(N'ALTER TABLE [$(Schema)].[hearings] ADD CONSTRAINT [FK_hearings_deleted_by_user] FOREIGN KEY ([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users] ([id]);');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).case_notes') AND name=N'IX_case_notes_case_deleted')
    CREATE INDEX [IX_case_notes_case_deleted] ON [$(Schema)].[case_notes] ([case_id],[is_deleted]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).hearings') AND name=N'IX_hearings_case_deleted')
    CREATE INDEX [IX_hearings_case_deleted] ON [$(Schema)].[hearings] ([case_id],[is_deleted]);
