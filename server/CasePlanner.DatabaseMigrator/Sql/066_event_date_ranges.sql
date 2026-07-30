SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'$(Schema).hearings') AND name=N'end_date')
BEGIN
    ALTER TABLE [$(Schema)].[hearings] ADD [end_date] date NULL;
END;
IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'$(Schema).hearings') AND name=N'start_time')
BEGIN
    ALTER TABLE [$(Schema)].[hearings] ADD [start_time] nvarchar(20) NULL;
END;
IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'$(Schema).hearings') AND name=N'end_time')
BEGIN
    ALTER TABLE [$(Schema)].[hearings] ADD [end_time] nvarchar(20) NULL;
END;
