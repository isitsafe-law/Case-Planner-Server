SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Manager/Administrator Dashboard Milestone 1 (audit/role foundation): manager_tier distinguishes
-- Deputy Chief Counsel from Chief Counsel within the existing is_manager population
-- (039_manager_flag.sql). Approval routing (decided separately, outside this migration) always
-- routes Filing Approval / Settlement Authority requests to Chief Counsel only, with no amount
-- threshold and no Administrator override - Deputy Chief Counsel gets read/visibility access to
-- approval queues but never an approve/grant/deny action. So this column exists purely to gate
-- UI/action buttons in a later milestone; this migration just adds the field. NULL means "no tier"
-- (an ordinary Manager, or a non-manager). Allowed non-null values ("DeputyChiefCounsel",
-- "ChiefCounsel") are enforced in C# (SqlServerCaseAssignmentRepository.SetUserManagerTierAsync),
-- not a DB CHECK constraint - same convention as is_manager's app-level-only validation. There is no
-- live SQL Server sandbox available here to exercise this against a real pilot instance - same
-- caveat already noted for the rest of the dormant multi-user foundation.

IF COL_LENGTH(N'$(Schema).app_users', N'manager_tier') IS NULL
    ALTER TABLE [$(Schema)].[app_users] ADD [manager_tier] nvarchar(30) NULL;
