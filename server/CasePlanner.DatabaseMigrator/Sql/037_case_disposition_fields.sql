SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Multi-user rollout Phase 5 (reporting), data-capture track. Per the rollout plan doc, this track
-- has zero auth/identity dependency (plain case fields, not roster/assignment-dependent), so unlike
-- several other Phase 1-4 pieces it gets full, normal dual-provider parity - this migration's
-- SQLite equivalent is the same AddColumnIfMissingAsync pattern used for every other optional case
-- column (see CasePlannerRepository.InitializeAsync). There is no live SQL Server sandbox available
-- here to exercise this against a real pilot instance - same limitation already noted for every
-- other migration file in this repo; this one has been reviewed for consistency with its siblings
-- (COL_LENGTH-guarded ALTER TABLE ADD, same as 002_case_soft_delete.sql / 022_case_lifecycle_dates.sql)
-- but not executed live.
--
-- All four columns stay nullable with no CHECK constraint, even though disposition_type/taking_type
-- have a fixed vocabulary (Jury Trial/Settlement/Mediation and Partial/Full/TCE respectively) and
-- final_judgment_amount/disposition_type are treated as required by the client's Close Case dialog -
-- that's a client-side UX rule, not a DB constraint, so existing rows and CSV/Excel imports that
-- predate these fields keep working unchanged.

IF COL_LENGTH(N'$(Schema).cases', N'final_judgment_amount') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [final_judgment_amount] decimal(18,2) NULL;

IF COL_LENGTH(N'$(Schema).cases', N'disposition_type') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [disposition_type] nvarchar(50) NULL;

IF COL_LENGTH(N'$(Schema).cases', N'taking_type') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [taking_type] nvarchar(20) NULL;

IF COL_LENGTH(N'$(Schema).cases', N'district') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [district] nvarchar(20) NULL;
