SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF COL_LENGTH(N'$(Schema).checklist_templates',N'row_version') IS NULL ALTER TABLE [$(Schema)].[checklist_templates] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).checklist_templates',N'created_by_user_id') IS NULL ALTER TABLE [$(Schema)].[checklist_templates] ADD [created_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).checklist_templates',N'updated_by_user_id') IS NULL ALTER TABLE [$(Schema)].[checklist_templates] ADD [updated_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).checklist_templates',N'is_deleted') IS NULL ALTER TABLE [$(Schema)].[checklist_templates] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_checklist_templates_is_deleted] DEFAULT(0);
IF COL_LENGTH(N'$(Schema).checklist_templates',N'deleted_utc') IS NULL ALTER TABLE [$(Schema)].[checklist_templates] ADD [deleted_utc] datetime2 NULL;
IF COL_LENGTH(N'$(Schema).checklist_templates',N'deleted_by_user_id') IS NULL ALTER TABLE [$(Schema)].[checklist_templates] ADD [deleted_by_user_id] uniqueidentifier NULL;

IF COL_LENGTH(N'$(Schema).checklist_template_items',N'row_version') IS NULL ALTER TABLE [$(Schema)].[checklist_template_items] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).checklist_template_items',N'created_by_user_id') IS NULL ALTER TABLE [$(Schema)].[checklist_template_items] ADD [created_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).checklist_template_items',N'updated_by_user_id') IS NULL ALTER TABLE [$(Schema)].[checklist_template_items] ADD [updated_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).checklist_template_items',N'is_deleted') IS NULL ALTER TABLE [$(Schema)].[checklist_template_items] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_checklist_template_items_is_deleted] DEFAULT(0);
IF COL_LENGTH(N'$(Schema).checklist_template_items',N'deleted_utc') IS NULL ALTER TABLE [$(Schema)].[checklist_template_items] ADD [deleted_utc] datetime2 NULL;
IF COL_LENGTH(N'$(Schema).checklist_template_items',N'deleted_by_user_id') IS NULL ALTER TABLE [$(Schema)].[checklist_template_items] ADD [deleted_by_user_id] uniqueidentifier NULL;

IF COL_LENGTH(N'$(Schema).deadline_templates',N'row_version') IS NULL ALTER TABLE [$(Schema)].[deadline_templates] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).deadline_templates',N'created_by_user_id') IS NULL ALTER TABLE [$(Schema)].[deadline_templates] ADD [created_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).deadline_templates',N'updated_by_user_id') IS NULL ALTER TABLE [$(Schema)].[deadline_templates] ADD [updated_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).deadline_templates',N'is_deleted') IS NULL ALTER TABLE [$(Schema)].[deadline_templates] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_deadline_templates_is_deleted] DEFAULT(0);
IF COL_LENGTH(N'$(Schema).deadline_templates',N'deleted_utc') IS NULL ALTER TABLE [$(Schema)].[deadline_templates] ADD [deleted_utc] datetime2 NULL;
IF COL_LENGTH(N'$(Schema).deadline_templates',N'deleted_by_user_id') IS NULL ALTER TABLE [$(Schema)].[deadline_templates] ADD [deleted_by_user_id] uniqueidentifier NULL;

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_checklist_templates_creator') EXEC(N'ALTER TABLE [$(Schema)].[checklist_templates] ADD CONSTRAINT [FK_checklist_templates_creator] FOREIGN KEY([created_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_checklist_templates_updater') EXEC(N'ALTER TABLE [$(Schema)].[checklist_templates] ADD CONSTRAINT [FK_checklist_templates_updater] FOREIGN KEY([updated_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_checklist_templates_deleter') EXEC(N'ALTER TABLE [$(Schema)].[checklist_templates] ADD CONSTRAINT [FK_checklist_templates_deleter] FOREIGN KEY([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_checklist_template_items_creator') EXEC(N'ALTER TABLE [$(Schema)].[checklist_template_items] ADD CONSTRAINT [FK_checklist_template_items_creator] FOREIGN KEY([created_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_checklist_template_items_updater') EXEC(N'ALTER TABLE [$(Schema)].[checklist_template_items] ADD CONSTRAINT [FK_checklist_template_items_updater] FOREIGN KEY([updated_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_checklist_template_items_deleter') EXEC(N'ALTER TABLE [$(Schema)].[checklist_template_items] ADD CONSTRAINT [FK_checklist_template_items_deleter] FOREIGN KEY([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_deadline_templates_creator') EXEC(N'ALTER TABLE [$(Schema)].[deadline_templates] ADD CONSTRAINT [FK_deadline_templates_creator] FOREIGN KEY([created_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_deadline_templates_updater') EXEC(N'ALTER TABLE [$(Schema)].[deadline_templates] ADD CONSTRAINT [FK_deadline_templates_updater] FOREIGN KEY([updated_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_deadline_templates_deleter') EXEC(N'ALTER TABLE [$(Schema)].[deadline_templates] ADD CONSTRAINT [FK_deadline_templates_deleter] FOREIGN KEY([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).checklist_templates') AND name=N'IX_checklist_templates_deleted') CREATE INDEX [IX_checklist_templates_deleted] ON [$(Schema)].[checklist_templates]([is_deleted],[id]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).checklist_template_items') AND name=N'IX_checklist_template_items_template_deleted') CREATE INDEX [IX_checklist_template_items_template_deleted] ON [$(Schema)].[checklist_template_items]([template_id],[is_deleted]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).deadline_templates') AND name=N'IX_deadline_templates_deleted') CREATE INDEX [IX_deadline_templates_deleted] ON [$(Schema)].[deadline_templates]([is_deleted],[id]);
