SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF COL_LENGTH(N'$(Schema).risk_analyses',N'row_version') IS NULL ALTER TABLE [$(Schema)].[risk_analyses] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).risk_analyses',N'created_by_user_id') IS NULL ALTER TABLE [$(Schema)].[risk_analyses] ADD [created_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).risk_analyses',N'updated_by_user_id') IS NULL ALTER TABLE [$(Schema)].[risk_analyses] ADD [updated_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).risk_analyses',N'is_deleted') IS NULL ALTER TABLE [$(Schema)].[risk_analyses] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_risk_analyses_is_deleted] DEFAULT(0);
IF COL_LENGTH(N'$(Schema).risk_analyses',N'deleted_utc') IS NULL ALTER TABLE [$(Schema)].[risk_analyses] ADD [deleted_utc] datetime2 NULL;
IF COL_LENGTH(N'$(Schema).risk_analyses',N'deleted_by_user_id') IS NULL ALTER TABLE [$(Schema)].[risk_analyses] ADD [deleted_by_user_id] uniqueidentifier NULL;

IF COL_LENGTH(N'$(Schema).risk_analysis_history',N'row_version') IS NULL ALTER TABLE [$(Schema)].[risk_analysis_history] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).risk_analysis_history',N'created_by_user_id') IS NULL ALTER TABLE [$(Schema)].[risk_analysis_history] ADD [created_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).risk_analysis_history',N'is_deleted') IS NULL ALTER TABLE [$(Schema)].[risk_analysis_history] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_risk_analysis_history_is_deleted] DEFAULT(0);
IF COL_LENGTH(N'$(Schema).risk_analysis_history',N'deleted_utc') IS NULL ALTER TABLE [$(Schema)].[risk_analysis_history] ADD [deleted_utc] datetime2 NULL;
IF COL_LENGTH(N'$(Schema).risk_analysis_history',N'deleted_by_user_id') IS NULL ALTER TABLE [$(Schema)].[risk_analysis_history] ADD [deleted_by_user_id] uniqueidentifier NULL;

IF COL_LENGTH(N'$(Schema).risk_analysis_offer_log',N'row_version') IS NULL ALTER TABLE [$(Schema)].[risk_analysis_offer_log] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).risk_analysis_offer_log',N'created_by_user_id') IS NULL ALTER TABLE [$(Schema)].[risk_analysis_offer_log] ADD [created_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).risk_analysis_offer_log',N'updated_by_user_id') IS NULL ALTER TABLE [$(Schema)].[risk_analysis_offer_log] ADD [updated_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).risk_analysis_offer_log',N'is_deleted') IS NULL ALTER TABLE [$(Schema)].[risk_analysis_offer_log] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_risk_analysis_offer_log_is_deleted] DEFAULT(0);
IF COL_LENGTH(N'$(Schema).risk_analysis_offer_log',N'deleted_utc') IS NULL ALTER TABLE [$(Schema)].[risk_analysis_offer_log] ADD [deleted_utc] datetime2 NULL;
IF COL_LENGTH(N'$(Schema).risk_analysis_offer_log',N'deleted_by_user_id') IS NULL ALTER TABLE [$(Schema)].[risk_analysis_offer_log] ADD [deleted_by_user_id] uniqueidentifier NULL;

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_risk_analyses_creator') EXEC(N'ALTER TABLE [$(Schema)].[risk_analyses] ADD CONSTRAINT [FK_risk_analyses_creator] FOREIGN KEY([created_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_risk_analyses_updater') EXEC(N'ALTER TABLE [$(Schema)].[risk_analyses] ADD CONSTRAINT [FK_risk_analyses_updater] FOREIGN KEY([updated_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_risk_analyses_deleter') EXEC(N'ALTER TABLE [$(Schema)].[risk_analyses] ADD CONSTRAINT [FK_risk_analyses_deleter] FOREIGN KEY([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_risk_analysis_history_creator') EXEC(N'ALTER TABLE [$(Schema)].[risk_analysis_history] ADD CONSTRAINT [FK_risk_analysis_history_creator] FOREIGN KEY([created_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_risk_analysis_history_deleter') EXEC(N'ALTER TABLE [$(Schema)].[risk_analysis_history] ADD CONSTRAINT [FK_risk_analysis_history_deleter] FOREIGN KEY([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_risk_offer_creator') EXEC(N'ALTER TABLE [$(Schema)].[risk_analysis_offer_log] ADD CONSTRAINT [FK_risk_offer_creator] FOREIGN KEY([created_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_risk_offer_updater') EXEC(N'ALTER TABLE [$(Schema)].[risk_analysis_offer_log] ADD CONSTRAINT [FK_risk_offer_updater] FOREIGN KEY([updated_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_risk_offer_deleter') EXEC(N'ALTER TABLE [$(Schema)].[risk_analysis_offer_log] ADD CONSTRAINT [FK_risk_offer_deleter] FOREIGN KEY([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).risk_analyses') AND name=N'IX_risk_analyses_case_deleted') CREATE INDEX [IX_risk_analyses_case_deleted] ON [$(Schema)].[risk_analyses]([case_id],[is_deleted]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).risk_analysis_history') AND name=N'IX_risk_analysis_history_case_deleted') CREATE INDEX [IX_risk_analysis_history_case_deleted] ON [$(Schema)].[risk_analysis_history]([case_id],[is_deleted]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).risk_analysis_offer_log') AND name=N'IX_risk_offer_case_deleted') CREATE INDEX [IX_risk_offer_case_deleted] ON [$(Schema)].[risk_analysis_offer_log]([case_id],[is_deleted]);
