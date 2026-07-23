SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Test-build feedback batch, item 5: Arkansas circuit courts are organized into numbered
-- divisions with an assigned judge - standard case metadata this schema had no place for. Plain
-- text (no dropdown/lookup, unlike district which is a fixed 10-value enumeration), same
-- COL_LENGTH-guarded ALTER pattern as every other optional case column. There is no live SQL
-- Server sandbox available here to exercise this against a real pilot instance - same limitation
-- already noted for every other migration file in this repo; this one has been reviewed for
-- consistency with its siblings but not executed live.

IF COL_LENGTH(N'$(Schema).cases', N'judge') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [judge] nvarchar(200) NULL;

IF COL_LENGTH(N'$(Schema).cases', N'division') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [division] nvarchar(100) NULL;
