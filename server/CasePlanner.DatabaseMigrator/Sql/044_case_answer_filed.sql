SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Eminent-domain condemnation cases routinely end up in a "default judgment" posture - most
-- commonly when serving a group of heirs, some or all of whom never answer. Before this migration
-- nothing in the schema tracked whether an answer or appearance had ever been filed; the only
-- existing signal was the generic "No-Answer Default Judgment Checkpoint" deadline template
-- (DeadlineTemplateSeeds in CasePlannerRepository.cs), which just showed up in the deadline list
-- like any other reminder with no dashboard visibility. answer_filed/answer_filed_date is the
-- missing fact - a manually-set boolean + a plain date string, mirroring service_perfected/
-- service_perfected_date exactly (same COL_LENGTH-guarded ALTER pattern as
-- 002_case_soft_delete.sql / 022_case_lifecycle_dates.sql / 029_event_status_trial_end_property_
-- description.sql, and the same nvarchar(20) date-string convention as date_opened/trial_end_date
-- in those two files - dates are stored as yyyy-MM-dd strings throughout this app, not native
-- date/datetime2). A visible warning badge (case list + case workspace header) is then derived
-- from this fact at read time by DefaultPostureCalculator - deliberately not another manually-set
-- status field (a prior cases.track field tried that shape and ended up permanently orphaned from
-- the UI). There is no live SQL Server sandbox available here to exercise this against a real
-- pilot instance - same limitation already noted for every other migration file in this repo; this
-- one has been reviewed for consistency with its siblings but not executed live.

IF COL_LENGTH(N'$(Schema).cases', N'answer_filed') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [answer_filed] bit NOT NULL CONSTRAINT [DF_cases_answer_filed] DEFAULT(0);

IF COL_LENGTH(N'$(Schema).cases', N'answer_filed_date') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [answer_filed_date] nvarchar(20) NULL;
