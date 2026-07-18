SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF COL_LENGTH(N'$(Schema).case_issue_tags',N'row_version') IS NULL ALTER TABLE [$(Schema)].[case_issue_tags] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).case_issue_tags',N'created_by_user_id') IS NULL ALTER TABLE [$(Schema)].[case_issue_tags] ADD [created_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).case_issue_tags',N'is_deleted') IS NULL ALTER TABLE [$(Schema)].[case_issue_tags] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_case_issue_tags_is_deleted] DEFAULT(0);
IF COL_LENGTH(N'$(Schema).case_issue_tags',N'deleted_utc') IS NULL ALTER TABLE [$(Schema)].[case_issue_tags] ADD [deleted_utc] datetime2 NULL;
IF COL_LENGTH(N'$(Schema).case_issue_tags',N'deleted_by_user_id') IS NULL ALTER TABLE [$(Schema)].[case_issue_tags] ADD [deleted_by_user_id] uniqueidentifier NULL;

IF COL_LENGTH(N'$(Schema).discovery_generations',N'created_by_user_id') IS NULL ALTER TABLE [$(Schema)].[discovery_generations] ADD [created_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).discovery_generations',N'row_version') IS NULL ALTER TABLE [$(Schema)].[discovery_generations] ADD [row_version] rowversion NOT NULL;

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_case_issue_tags_creator') EXEC(N'ALTER TABLE [$(Schema)].[case_issue_tags] ADD CONSTRAINT [FK_case_issue_tags_creator] FOREIGN KEY([created_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_case_issue_tags_deleter') EXEC(N'ALTER TABLE [$(Schema)].[case_issue_tags] ADD CONSTRAINT [FK_case_issue_tags_deleter] FOREIGN KEY([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_discovery_generations_creator') EXEC(N'ALTER TABLE [$(Schema)].[discovery_generations] ADD CONSTRAINT [FK_discovery_generations_creator] FOREIGN KEY([created_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).case_issue_tags') AND name=N'IX_case_issue_tags_case_deleted') EXEC(N'CREATE INDEX [IX_case_issue_tags_case_deleted] ON [$(Schema)].[case_issue_tags]([case_id],[is_deleted]);');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).case_issue_tags') AND name=N'UX_case_issue_tags_active') EXEC(N'CREATE UNIQUE INDEX [UX_case_issue_tags_active] ON [$(Schema)].[case_issue_tags]([case_id],[issue_tag_id]) WHERE [is_deleted]=0;');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).discovery_generations') AND name=N'IX_discovery_generations_case') EXEC(N'CREATE INDEX [IX_discovery_generations_case] ON [$(Schema)].[discovery_generations]([case_id]);');
