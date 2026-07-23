SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Test-build feedback batch, items 2 & 3: the Close Case dialog (CloseCaseDialog.tsx) already
-- captures closed_date/disposition_type/final_judgment_amount (037_case_disposition_fields.sql)
-- at the Active/Triage -> Closed transition; attorney_fees_awarded/attorney_fees_amount are two
-- more plain facts captured in that same flow, same COL_LENGTH-guarded ALTER pattern and same
-- nullable-at-the-DB-level convention as final_judgment_amount/disposition_type in that file (the
-- client's canSubmit gating around the checkbox/amount is a UX rule, not a DB constraint). This is
-- a plain manually-entered fact, not a computed/validated one - it does not check the statutory
-- attorney's-fee-shift threshold (Ark. Code Ann. Sec. 27-67-317(b), jury-verdict-only) in any way;
-- that verification is already prompted separately by the "Post-Trial - Core" checklist template
-- (see TemplateSeeds in CasePlannerRepository.cs). There is no live SQL Server sandbox available
-- here to exercise this against a real pilot instance - same limitation already noted for every
-- other migration file in this repo; this one has been reviewed for consistency with its siblings
-- but not executed live.

IF COL_LENGTH(N'$(Schema).cases', N'attorney_fees_awarded') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [attorney_fees_awarded] bit NOT NULL CONSTRAINT [DF_cases_attorney_fees_awarded] DEFAULT(0);

IF COL_LENGTH(N'$(Schema).cases', N'attorney_fees_amount') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [attorney_fees_amount] decimal(18,2) NULL;
