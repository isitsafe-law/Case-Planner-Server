SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Multi-user rollout Phase 4a (notifications core): in-app only - two triggers, task assigned and
-- task completed (deadline reminders are Phase 4b, not built here; email delivery is also Phase
-- 4b). recipient_user_id follows checklist_items.assigned_user_id's precedent exactly - opaque
-- TEXT passthrough on SQLite (no app_users table there), uniqueidentifier FK'd to dbo.app_users
-- here. notification_type is a plain app-validated string (TaskAssigned/TaskCompleted today,
-- DeadlineReminder to follow in 4b) rather than a DB enum, matching case_role/assignment_role/
-- checklist status elsewhere in this schema. No soft-delete columns - unlike the audited entities
-- elsewhere in this schema, a notification row is read-once/ephemeral, not something that needs a
-- recoverable delete trail. There is no live SQL Server sandbox available here to exercise this
-- against a real pilot instance - same caveat already noted for the rest of the dormant
-- multi-user foundation.

IF OBJECT_ID(N'$(Schema).notifications','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[notifications]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_notifications] PRIMARY KEY,
        [recipient_user_id] uniqueidentifier NOT NULL,
        [case_id] bigint NULL,
        [notification_type] nvarchar(50) NOT NULL,
        [title] nvarchar(200) NOT NULL,
        [body] nvarchar(1000) NULL,
        [is_read] bit NOT NULL CONSTRAINT [DF_notifications_is_read] DEFAULT(0),
        [created_at] datetime2 NOT NULL CONSTRAINT [DF_notifications_created] DEFAULT(SYSUTCDATETIME()),
        [read_at] datetime2 NULL,
        [row_version] rowversion NOT NULL,
        CONSTRAINT [FK_notifications_recipient_user] FOREIGN KEY ([recipient_user_id]) REFERENCES [$(Schema)].[app_users] ([id]),
        CONSTRAINT [FK_notifications_case] FOREIGN KEY ([case_id]) REFERENCES [$(Schema)].[cases] ([id])
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).notifications') AND name=N'IX_notifications_recipient_read_created')
    CREATE INDEX [IX_notifications_recipient_read_created] ON [$(Schema)].[notifications] ([recipient_user_id],[is_read],[created_at] DESC);
