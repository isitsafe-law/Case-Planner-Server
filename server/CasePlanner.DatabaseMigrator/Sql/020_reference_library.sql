SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF OBJECT_ID(N'$(Schema).reference_library_documents','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[reference_library_documents]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_reference_library_documents] PRIMARY KEY,
        [document_key] nvarchar(80) NOT NULL,
        [title] nvarchar(400) NOT NULL,
        [description] nvarchar(1000) NULL,
        [document_text] nvarchar(max) NOT NULL,
        [is_deleted] bit NOT NULL CONSTRAINT [DF_reference_library_documents_deleted] DEFAULT(0),
        [created_utc] datetime2 NOT NULL CONSTRAINT [DF_reference_library_documents_created] DEFAULT(SYSUTCDATETIME()),
        [updated_utc] datetime2 NOT NULL CONSTRAINT [DF_reference_library_documents_updated] DEFAULT(SYSUTCDATETIME()),
        [row_version] rowversion NOT NULL
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).reference_library_documents') AND name=N'UX_reference_library_documents_key')
    CREATE UNIQUE INDEX [UX_reference_library_documents_key] ON [$(Schema)].[reference_library_documents]([document_key]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).reference_library_documents') AND name=N'IX_reference_library_documents_active')
    CREATE INDEX [IX_reference_library_documents_active] ON [$(Schema)].[reference_library_documents]([is_deleted],[title]);
