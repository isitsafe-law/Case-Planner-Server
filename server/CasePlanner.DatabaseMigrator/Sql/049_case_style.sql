SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Test-build feedback batch, item 7: the full case caption (e.g. "State of Arkansas ex rel.
-- Arkansas State Highway Commission v. John Doe, et al."), captured purely so it's fast to copy
-- into documents drafted elsewhere - see the client's "Copy Case Style" affordance. nvarchar(max)
-- because a caption with multiple named defendants/heirs can run long and is expected to be
-- multi-line. No document-generation merge/token coupling - this repo has no existing
-- case-field-to-document-token mechanism to hook into, so this is scoped as a field + client-side
-- copy affordance only. Same COL_LENGTH-guarded ALTER pattern as every other optional case column.
-- There is no live SQL Server sandbox available here to exercise this against a real pilot
-- instance - same limitation already noted for every other migration file in this repo; this one
-- has been reviewed for consistency with its siblings but not executed live.

IF COL_LENGTH(N'$(Schema).cases', N'case_style') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [case_style] nvarchar(max) NULL;
