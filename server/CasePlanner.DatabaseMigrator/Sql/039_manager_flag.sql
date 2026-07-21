SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Multi-user rollout Phase 6 (Staff Directory edit access): "Manager" is a new, separate tier that
-- may also edit the Staff Directory (038_staff_directory.sql), alongside Administrator. Unlike
-- is_administrator (035_administrator_flag.sql), which mirrors an Entra app-role claim at login and
-- has no in-app management UI, there is no Entra app role for Manager - the office wants it directly
-- togglable per person from the existing "Attorneys & Staff" admin screen, the same way is_active
-- already is. So is_manager is never written from claims; it is only ever changed by an
-- administrator, via PUT /api/admin/users/{userId}/manager, and simply read back at login/provision
-- time. Default 0/false. There is no live SQL Server sandbox available here to exercise this against
-- a real pilot instance - same caveat already noted for the rest of the dormant multi-user
-- foundation.

IF COL_LENGTH(N'$(Schema).app_users', N'is_manager') IS NULL
    ALTER TABLE [$(Schema)].[app_users] ADD [is_manager] bit NOT NULL CONSTRAINT [DF_app_users_is_manager] DEFAULT(0);
