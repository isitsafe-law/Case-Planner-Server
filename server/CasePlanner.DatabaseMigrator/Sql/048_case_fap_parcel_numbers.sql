SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Test-build feedback batch, item 6: two additional case identifiers, both plain text with no
-- lookup/validation, and both purely additive - job_number/tract remain the primary identifier
-- pair and are untouched by this migration. fap_number is the Federal Aid Project number;
-- parcel_number is the county assessor/collector's parcel identifier, used when notifying that
-- office about the case action. Same COL_LENGTH-guarded ALTER pattern as every other optional
-- case column. There is no live SQL Server sandbox available here to exercise this against a real
-- pilot instance - same limitation already noted for every other migration file in this repo; this
-- one has been reviewed for consistency with its siblings but not executed live.

IF COL_LENGTH(N'$(Schema).cases', N'fap_number') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [fap_number] nvarchar(100) NULL;

IF COL_LENGTH(N'$(Schema).cases', N'parcel_number') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [parcel_number] nvarchar(100) NULL;
