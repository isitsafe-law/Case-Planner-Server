SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Manager/Administrator Dashboard Milestone 3: the ceiling Chief Counsel has most recently granted
-- via an approved settlement_authority_requests row (058_settlement_authority_requests.sql). Same
-- COL_LENGTH-guarded decimal(18,2) NULL convention as attorney_fees_amount
-- (046_case_attorney_fees.sql) - a plain fact stamped by ISettlementAuthorityRequestStore.DecideAsync
-- when Action="Approved", left untouched by Denied/InfoRequested decisions. Null means "no
-- settlement authority ceiling has ever been granted for this case" - it is overwritten (not
-- appended) by a later approval; the full history of every decision lives in activity_log via the
-- diff columns added in 057_activity_log_role_and_diff.sql. Feeds the previously-dead
-- TrialWatchEntry.SettlementAuthority dashboard field. There is no live SQL Server sandbox
-- available here to exercise this against a real pilot instance - same caveat already noted for
-- every other migration file in this repo; this one has been reviewed for consistency with its
-- siblings but not executed live.

IF COL_LENGTH(N'$(Schema).cases', N'settlement_authorized_ceiling') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [settlement_authorized_ceiling] decimal(18,2) NULL;
