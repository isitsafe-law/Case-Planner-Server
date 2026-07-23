SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Test-build feedback batch, item 8: opposing counsel's phone/email/address as free text, not
-- separate structured fields - kept simple to match how the existing opposing-counsel name data
-- (cases.opposing_counsel, and the newer case_opposing_attorneys child table) is already just a
-- plain string with no address-book structure. nvarchar(max) because this can be multi-line
-- (phone + email + mailing address together). Same COL_LENGTH-guarded ALTER pattern as every other
-- optional case column. There is no live SQL Server sandbox available here to exercise this
-- against a real pilot instance - same limitation already noted for every other migration file in
-- this repo; this one has been reviewed for consistency with its siblings but not executed live.

IF COL_LENGTH(N'$(Schema).cases', N'opposing_counsel_contact') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [opposing_counsel_contact] nvarchar(max) NULL;
