SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF OBJECT_ID(N'$(Schema).custom_document_templates','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[custom_document_templates]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_custom_document_templates] PRIMARY KEY,
        [template_key] nvarchar(260) NOT NULL,
        [base_key] nvarchar(220) NOT NULL,
        [title] nvarchar(400) NOT NULL,
        [file_name] nvarchar(260) NOT NULL,
        [storage_path] nvarchar(2048) NOT NULL,
        [format] nvarchar(20) NOT NULL,
        [version] int NOT NULL,
        [tokens_json] nvarchar(max) NOT NULL,
        [unknown_tokens_json] nvarchar(max) NOT NULL,
        [is_active] bit NOT NULL CONSTRAINT [DF_custom_document_templates_active] DEFAULT(0),
        [uploaded_at] nvarchar(50) NOT NULL,
        [uploaded_by_user_id] uniqueidentifier NULL,
        [uploaded_by_display] nvarchar(400) NULL,
        [row_version] rowversion NOT NULL,
        [is_deleted] bit NOT NULL CONSTRAINT [DF_custom_document_templates_deleted] DEFAULT(0),
        [deleted_utc] datetime2 NULL,
        [deleted_by_user_id] uniqueidentifier NULL,
        CONSTRAINT [FK_custom_document_templates_uploader] FOREIGN KEY([uploaded_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]),
        CONSTRAINT [FK_custom_document_templates_deleter] FOREIGN KEY([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users]([id])
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).custom_document_templates') AND name=N'UX_custom_document_templates_key')
    CREATE UNIQUE INDEX [UX_custom_document_templates_key] ON [$(Schema)].[custom_document_templates]([template_key]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).custom_document_templates') AND name=N'UX_custom_document_templates_base_version')
    CREATE UNIQUE INDEX [UX_custom_document_templates_base_version] ON [$(Schema)].[custom_document_templates]([base_key],[version]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).custom_document_templates') AND name=N'UX_custom_document_templates_active')
    CREATE UNIQUE INDEX [UX_custom_document_templates_active] ON [$(Schema)].[custom_document_templates]([base_key]) WHERE [is_active]=1 AND [is_deleted]=0;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).custom_document_templates') AND name=N'IX_custom_document_templates_deleted')
    CREATE INDEX [IX_custom_document_templates_deleted] ON [$(Schema)].[custom_document_templates]([is_deleted],[base_key],[version]);
