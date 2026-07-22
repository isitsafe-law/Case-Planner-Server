SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Notifications gap fix: today nothing can address a notification to "the case's Attorney" or
-- "the case's Legal Assistant" by name, because the Staff Directory (038_staff_directory.sql -
-- plain name records, zero auth/identity dependency) has no connection to dbo.app_users (the real
-- Entra-login identity table that notifications are actually keyed to). linked_user_id is a
-- nullable, manually-set link from a Staff Directory row to the real account it corresponds to -
-- FK'd to dbo.app_users like every other actor reference in the SQL Server pilot schema, following
-- the exact opaque-passthrough convention already used for checklist_items.assigned_user_id
-- (032_checklist_item_assignment.sql): a plain string on SQLite (no app_users table there to
-- validate against), a real uniqueidentifier + FK here. Deliberately never auto-matched by name or
-- email - an office may have a Staff Directory name that doesn't correspond 1:1 to a single Entra
-- account (or none at all yet), so this is only ever set by an admin/manager, by hand, from the
-- Attorneys & Staff screen. There is no live SQL Server sandbox available here to exercise this
-- against a real pilot instance - same caveat already noted for the rest of the dormant multi-user
-- foundation.

IF COL_LENGTH(N'$(Schema).attorneys', N'linked_user_id') IS NULL
    ALTER TABLE [$(Schema)].[attorneys] ADD [linked_user_id] uniqueidentifier NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_attorneys_linked_user')
    EXEC(N'ALTER TABLE [$(Schema)].[attorneys] ADD CONSTRAINT [FK_attorneys_linked_user] FOREIGN KEY ([linked_user_id]) REFERENCES [$(Schema)].[app_users] ([id]);');

IF COL_LENGTH(N'$(Schema).legal_assistants', N'linked_user_id') IS NULL
    ALTER TABLE [$(Schema)].[legal_assistants] ADD [linked_user_id] uniqueidentifier NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_legal_assistants_linked_user')
    EXEC(N'ALTER TABLE [$(Schema)].[legal_assistants] ADD CONSTRAINT [FK_legal_assistants_linked_user] FOREIGN KEY ([linked_user_id]) REFERENCES [$(Schema)].[app_users] ([id]);');
