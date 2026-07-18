SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF COL_LENGTH(N'$(Schema).activity_log',N'actor_user_id') IS NULL ALTER TABLE [$(Schema)].[activity_log] ADD [actor_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).activity_log',N'actor_display') IS NULL ALTER TABLE [$(Schema)].[activity_log] ADD [actor_display] nvarchar(400) NULL;
IF COL_LENGTH(N'$(Schema).activity_log',N'row_version') IS NULL ALTER TABLE [$(Schema)].[activity_log] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).activity_log_history',N'edited_by_user_id') IS NULL ALTER TABLE [$(Schema)].[activity_log_history] ADD [edited_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).activity_log_history',N'edited_by_display') IS NULL ALTER TABLE [$(Schema)].[activity_log_history] ADD [edited_by_display] nvarchar(400) NULL;

IF COL_LENGTH(N'$(Schema).document_exports',N'created_by_user_id') IS NULL ALTER TABLE [$(Schema)].[document_exports] ADD [created_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).document_exports',N'created_by_display') IS NULL ALTER TABLE [$(Schema)].[document_exports] ADD [created_by_display] nvarchar(400) NULL;
IF COL_LENGTH(N'$(Schema).document_exports',N'qa_reviewed_by_user_id') IS NULL ALTER TABLE [$(Schema)].[document_exports] ADD [qa_reviewed_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).document_exports',N'qa_reviewed_by_display') IS NULL ALTER TABLE [$(Schema)].[document_exports] ADD [qa_reviewed_by_display] nvarchar(400) NULL;
IF COL_LENGTH(N'$(Schema).document_exports',N'merge_field_values') IS NULL ALTER TABLE [$(Schema)].[document_exports] ADD [merge_field_values] nvarchar(max) NULL;
IF COL_LENGTH(N'$(Schema).document_exports',N'row_version') IS NULL ALTER TABLE [$(Schema)].[document_exports] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).document_exports',N'is_deleted') IS NULL ALTER TABLE [$(Schema)].[document_exports] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_document_exports_is_deleted] DEFAULT(0);
IF COL_LENGTH(N'$(Schema).document_exports',N'deleted_utc') IS NULL ALTER TABLE [$(Schema)].[document_exports] ADD [deleted_utc] datetime2 NULL;
IF COL_LENGTH(N'$(Schema).document_exports',N'deleted_by_user_id') IS NULL ALTER TABLE [$(Schema)].[document_exports] ADD [deleted_by_user_id] uniqueidentifier NULL;

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_activity_log_actor') EXEC(N'ALTER TABLE [$(Schema)].[activity_log] ADD CONSTRAINT [FK_activity_log_actor] FOREIGN KEY([actor_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_activity_log_history_editor') EXEC(N'ALTER TABLE [$(Schema)].[activity_log_history] ADD CONSTRAINT [FK_activity_log_history_editor] FOREIGN KEY([edited_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_document_exports_creator') EXEC(N'ALTER TABLE [$(Schema)].[document_exports] ADD CONSTRAINT [FK_document_exports_creator] FOREIGN KEY([created_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_document_exports_qa_reviewer') EXEC(N'ALTER TABLE [$(Schema)].[document_exports] ADD CONSTRAINT [FK_document_exports_qa_reviewer] FOREIGN KEY([qa_reviewed_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_document_exports_deleter') EXEC(N'ALTER TABLE [$(Schema)].[document_exports] ADD CONSTRAINT [FK_document_exports_deleter] FOREIGN KEY([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).activity_log') AND name=N'IX_activity_log_case') CREATE INDEX [IX_activity_log_case] ON [$(Schema)].[activity_log]([case_id]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).document_exports') AND name=N'IX_document_exports_case_deleted') CREATE INDEX [IX_document_exports_case_deleted] ON [$(Schema)].[document_exports]([case_id],[is_deleted]);
