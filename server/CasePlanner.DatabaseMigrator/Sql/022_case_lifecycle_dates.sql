SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF COL_LENGTH(N'$(Schema).cases',N'date_opened') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [date_opened] nvarchar(20) NULL;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).cases') AND name=N'IX_cases_date_opened')
    CREATE INDEX [IX_cases_date_opened] ON [$(Schema)].[cases]([date_opened]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).cases') AND name=N'IX_cases_closed_date')
    CREATE INDEX [IX_cases_closed_date] ON [$(Schema)].[cases]([closed_date]);
