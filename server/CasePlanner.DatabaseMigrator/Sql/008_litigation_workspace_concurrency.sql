SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF COL_LENGTH(N'$(Schema).witnesses', N'row_version') IS NULL ALTER TABLE [$(Schema)].[witnesses] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).witnesses', N'is_deleted') IS NULL ALTER TABLE [$(Schema)].[witnesses] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_witnesses_is_deleted] DEFAULT (0);
IF COL_LENGTH(N'$(Schema).witnesses', N'deleted_utc') IS NULL ALTER TABLE [$(Schema)].[witnesses] ADD [deleted_utc] datetime2 NULL;
IF COL_LENGTH(N'$(Schema).witnesses', N'deleted_by_user_id') IS NULL ALTER TABLE [$(Schema)].[witnesses] ADD [deleted_by_user_id] uniqueidentifier NULL;

IF COL_LENGTH(N'$(Schema).exhibits', N'row_version') IS NULL ALTER TABLE [$(Schema)].[exhibits] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).exhibits', N'is_deleted') IS NULL ALTER TABLE [$(Schema)].[exhibits] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_exhibits_is_deleted] DEFAULT (0);
IF COL_LENGTH(N'$(Schema).exhibits', N'deleted_utc') IS NULL ALTER TABLE [$(Schema)].[exhibits] ADD [deleted_utc] datetime2 NULL;
IF COL_LENGTH(N'$(Schema).exhibits', N'deleted_by_user_id') IS NULL ALTER TABLE [$(Schema)].[exhibits] ADD [deleted_by_user_id] uniqueidentifier NULL;

IF COL_LENGTH(N'$(Schema).trial_motions', N'row_version') IS NULL ALTER TABLE [$(Schema)].[trial_motions] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).trial_motions', N'is_deleted') IS NULL ALTER TABLE [$(Schema)].[trial_motions] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_trial_motions_is_deleted] DEFAULT (0);
IF COL_LENGTH(N'$(Schema).trial_motions', N'deleted_utc') IS NULL ALTER TABLE [$(Schema)].[trial_motions] ADD [deleted_utc] datetime2 NULL;
IF COL_LENGTH(N'$(Schema).trial_motions', N'deleted_by_user_id') IS NULL ALTER TABLE [$(Schema)].[trial_motions] ADD [deleted_by_user_id] uniqueidentifier NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_witnesses_deleted_by_user') EXEC(N'ALTER TABLE [$(Schema)].[witnesses] ADD CONSTRAINT [FK_witnesses_deleted_by_user] FOREIGN KEY ([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users] ([id]);');
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_exhibits_deleted_by_user') EXEC(N'ALTER TABLE [$(Schema)].[exhibits] ADD CONSTRAINT [FK_exhibits_deleted_by_user] FOREIGN KEY ([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users] ([id]);');
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_trial_motions_deleted_by_user') EXEC(N'ALTER TABLE [$(Schema)].[trial_motions] ADD CONSTRAINT [FK_trial_motions_deleted_by_user] FOREIGN KEY ([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users] ([id]);');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).witnesses') AND name=N'IX_witnesses_case_deleted') CREATE INDEX [IX_witnesses_case_deleted] ON [$(Schema)].[witnesses] ([case_id],[is_deleted]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).exhibits') AND name=N'IX_exhibits_case_deleted') CREATE INDEX [IX_exhibits_case_deleted] ON [$(Schema)].[exhibits] ([case_id],[is_deleted]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).trial_motions') AND name=N'IX_trial_motions_case_deleted') CREATE INDEX [IX_trial_motions_case_deleted] ON [$(Schema)].[trial_motions] ([case_id],[is_deleted]);
