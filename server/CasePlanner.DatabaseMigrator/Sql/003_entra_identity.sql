SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

IF COL_LENGTH(N'$(Schema).app_users', N'entra_tenant_id') IS NULL
    ALTER TABLE [$(Schema)].[app_users] ADD [entra_tenant_id] uniqueidentifier NULL;

IF COL_LENGTH(N'$(Schema).app_users', N'entra_object_id') IS NULL
    ALTER TABLE [$(Schema)].[app_users] ADD [entra_object_id] uniqueidentifier NULL;

IF COL_LENGTH(N'$(Schema).app_users', N'last_login_utc') IS NULL
    ALTER TABLE [$(Schema)].[app_users] ADD [last_login_utc] datetime2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'$(Schema).app_users') AND name = N'UX_app_users_entra_identity')
    EXEC(N'CREATE UNIQUE INDEX [UX_app_users_entra_identity]
        ON [$(Schema)].[app_users] ([entra_tenant_id], [entra_object_id])
        WHERE [entra_tenant_id] IS NOT NULL AND [entra_object_id] IS NOT NULL;');
