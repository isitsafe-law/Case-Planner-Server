SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- This pass's additions (schema-only, zero doc-gen token dependency):
--   1) hearings.status - event status vocabulary (Scheduled/Completed/Continued/Canceled),
--      following the same "hearings/HearingRecord unchanged, only vocabulary added" precedent
--      as event_type in 028_case_events.sql. Existing rows backfill to 'Scheduled'.
--   2) cases.trial_end_date - optional end date for a multi-day jury trial. Purely descriptive/
--      display; the dashboard triage engine, attorney dashboard engine, and deadline-template
--      trigger all correctly continue to key off trial_date (the start date) only.
--   3) cases.property_description - free-text description of the property at issue.

IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'$(Schema).hearings') AND name=N'status')
BEGIN
    ALTER TABLE [$(Schema)].[hearings] ADD [status] nvarchar(100) NOT NULL CONSTRAINT [DF_hearings_status] DEFAULT(N'Scheduled');
END;

IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'$(Schema).cases') AND name=N'trial_end_date')
BEGIN
    ALTER TABLE [$(Schema)].[cases] ADD [trial_end_date] nvarchar(20) NULL;
END;

IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'$(Schema).cases') AND name=N'property_description')
BEGIN
    ALTER TABLE [$(Schema)].[cases] ADD [property_description] nvarchar(max) NULL;
END;
