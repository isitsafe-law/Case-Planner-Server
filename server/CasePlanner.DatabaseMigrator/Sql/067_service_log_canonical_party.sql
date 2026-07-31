SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Additive bridge from historical free-text service parties to canonical case_defendants.
-- party_name remains the preserved snapshot and is never removed or overwritten by migration.
IF COL_LENGTH(N'$(Schema).service_log_entries', N'case_defendant_id') IS NULL
    ALTER TABLE [$(Schema)].[service_log_entries] ADD [case_defendant_id] bigint NULL;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).service_log_entries') AND name=N'IX_service_log_entries_defendant')
    CREATE INDEX [IX_service_log_entries_defendant] ON [$(Schema)].[service_log_entries]([case_defendant_id]);
