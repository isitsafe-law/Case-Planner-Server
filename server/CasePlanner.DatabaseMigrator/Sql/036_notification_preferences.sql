SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Multi-user rollout Phase 4c (notifications: per-user preferences). One row per person, six
-- independent booleans (in-app/email x TaskAssigned/TaskCompleted/DeadlineReminder), all default 1
-- (enabled) so notifications keep working out of the box with zero setup. user_id follows
-- notifications.recipient_user_id's exact opaque passthrough precedent - TEXT on SQLite (no
-- app_users table there, no FK), uniqueidentifier FK'd to dbo.app_users here. No row for a user
-- means all-defaults-true; a row only gets created once someone actually changes something (or the
-- app may choose to always upsert a full row on any save - the store's Upsert is a full replace
-- either way). Fully dual-provider testable, unlike Part A's is_administrator/admin-union pieces -
-- this table has no case_assignments dependency at all, it's a pure per-user-id row. There is no
-- live SQL Server sandbox available here to exercise this against a real pilot instance - same
-- caveat already noted for the rest of the dormant multi-user foundation.

IF OBJECT_ID(N'$(Schema).notification_preferences','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[notification_preferences]
    (
        [user_id] uniqueidentifier NOT NULL CONSTRAINT [PK_notification_preferences] PRIMARY KEY,
        [task_assigned_in_app] bit NOT NULL CONSTRAINT [DF_np_task_assigned_in_app] DEFAULT(1),
        [task_assigned_email] bit NOT NULL CONSTRAINT [DF_np_task_assigned_email] DEFAULT(1),
        [task_completed_in_app] bit NOT NULL CONSTRAINT [DF_np_task_completed_in_app] DEFAULT(1),
        [task_completed_email] bit NOT NULL CONSTRAINT [DF_np_task_completed_email] DEFAULT(1),
        [deadline_reminder_in_app] bit NOT NULL CONSTRAINT [DF_np_deadline_reminder_in_app] DEFAULT(1),
        [deadline_reminder_email] bit NOT NULL CONSTRAINT [DF_np_deadline_reminder_email] DEFAULT(1),
        [updated_at] datetime2 NULL,
        CONSTRAINT [FK_notification_preferences_user] FOREIGN KEY ([user_id]) REFERENCES [$(Schema)].[app_users] ([id])
    );
END;
