SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Multi-user rollout Item 2 (task assignment): checklist_items gains a nullable assignee, FK'd to
-- dbo.app_users like every other actor reference in the SQL Server pilot schema (see
-- SqlServerCaseAssignmentRepository's app_users usage). This is SQL-Server-only functional, same
-- as Phase 1's roster/assignment work - the SQLite side gets the same-named column as an opaque
-- passthrough (no app_users table there to validate against) purely so the column round-trips
-- structurally on both providers; it is only ever meaningfully populated/selectable once Entra is
-- enabled.

IF COL_LENGTH(N'$(Schema).checklist_items', N'assigned_user_id') IS NULL
    ALTER TABLE [$(Schema)].[checklist_items] ADD [assigned_user_id] uniqueidentifier NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_checklist_items_assigned_user')
    EXEC(N'ALTER TABLE [$(Schema)].[checklist_items] ADD CONSTRAINT [FK_checklist_items_assigned_user] FOREIGN KEY ([assigned_user_id]) REFERENCES [$(Schema)].[app_users] ([id]);');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).checklist_items') AND name=N'IX_checklist_items_assigned_user')
    CREATE INDEX [IX_checklist_items_assigned_user] ON [$(Schema)].[checklist_items] ([assigned_user_id]);
