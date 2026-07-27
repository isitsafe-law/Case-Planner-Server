SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Manager/Administrator Dashboard Milestone 1 (audit/role foundation): every activity_log write now
-- always stamps the actor's resolved role (IApplicationActorContext.Role) at the moment of the
-- action, alongside the existing actor_user_id/actor_display (010_activity_document_audit.sql). The
-- three diff columns are nullable and stay null for every ordinary activity write today - they exist
-- so a later milestone's manager-override call path can record a plain field-level before/after
-- (e.g. "Reassigned attorney: Jane Doe -> John Smith") without inventing a second logging table.
-- There is no live SQL Server sandbox available here to exercise this against a real pilot instance -
-- same caveat already noted for the rest of the dormant multi-user foundation.

IF COL_LENGTH(N'$(Schema).activity_log', N'actor_role_at_action') IS NULL
    ALTER TABLE [$(Schema)].[activity_log] ADD [actor_role_at_action] nvarchar(100) NULL;
IF COL_LENGTH(N'$(Schema).activity_log', N'field_changed') IS NULL
    ALTER TABLE [$(Schema)].[activity_log] ADD [field_changed] nvarchar(200) NULL;
IF COL_LENGTH(N'$(Schema).activity_log', N'previous_value') IS NULL
    ALTER TABLE [$(Schema)].[activity_log] ADD [previous_value] nvarchar(max) NULL;
IF COL_LENGTH(N'$(Schema).activity_log', N'new_value') IS NULL
    ALTER TABLE [$(Schema)].[activity_log] ADD [new_value] nvarchar(max) NULL;
