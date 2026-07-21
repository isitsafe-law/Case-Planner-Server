SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Multi-user rollout Phase 4c (notifications: admin system-wide inclusion). "Administrator" was
-- previously a pure per-request claims check (CaseAccessEvaluator.IsAdministrator, against the
-- current ClaimsPrincipal) with no durable row anywhere - fine for request-scoped access checks,
-- but useless to a BackgroundService like DeadlineReminderBackgroundService, which runs on a timer
-- with no ClaimsPrincipal to check. is_administrator makes "who are the current admins" a queryable
-- fact, populated at login time from the same Entra app-role claim
-- (SqlServerAppUserRepository.ProvisionAsync) rather than a live Graph lookup - "as of last login",
-- same accepted staleness window as last_login_utc itself. Not something an app user edits directly
-- anywhere; there is no admin-management UI for this column. There is no live SQL Server sandbox
-- available here to exercise this against a real pilot instance - same caveat already noted for the
-- rest of the dormant multi-user foundation.

IF COL_LENGTH(N'$(Schema).app_users', N'is_administrator') IS NULL
    ALTER TABLE [$(Schema)].[app_users] ADD [is_administrator] bit NOT NULL CONSTRAINT [DF_app_users_is_administrator] DEFAULT(0);
