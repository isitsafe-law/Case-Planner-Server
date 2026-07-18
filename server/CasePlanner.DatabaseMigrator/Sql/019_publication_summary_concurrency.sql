SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF COL_LENGTH(N'$(Schema).case_publications',N'row_version') IS NULL
    ALTER TABLE [$(Schema)].[case_publications] ADD [row_version] rowversion NOT NULL;

IF COL_LENGTH(N'$(Schema).case_publications',N'last_updated_by_user_id') IS NULL
    ALTER TABLE [$(Schema)].[case_publications] ADD [last_updated_by_user_id] uniqueidentifier NULL;

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_case_publications_last_updated_by_user')
    EXEC(N'ALTER TABLE [$(Schema)].[case_publications] ADD CONSTRAINT [FK_case_publications_last_updated_by_user] FOREIGN KEY([last_updated_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).case_publications') AND name=N'UX_case_publications_case')
    EXEC(N'CREATE UNIQUE INDEX [UX_case_publications_case] ON [$(Schema)].[case_publications]([case_id]);');
