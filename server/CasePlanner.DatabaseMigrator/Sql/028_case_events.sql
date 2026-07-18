SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Generalizes the case-level "Hearings" tab into "Events" (Group E of the search/dashboard/
-- case-record cleanup pass). Adds an event_type column to the existing hearings table rather
-- than renaming the table - hearings/HearingRecord feed the dashboard triage engine, both
-- work-queue aggregations, and their own SQL Server pilot store/reconciliation service, and
-- renaming all of those was judged excessive churn for what is fundamentally a UI relabeling
-- and vocabulary generalization, not a data-model replacement. Existing rows backfill to
-- 'Hearing' so no history is reinterpreted.

IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'$(Schema).hearings') AND name=N'event_type')
BEGIN
    ALTER TABLE [$(Schema)].[hearings] ADD [event_type] nvarchar(100) NOT NULL CONSTRAINT [DF_hearings_event_type] DEFAULT(N'Hearing');
END;
