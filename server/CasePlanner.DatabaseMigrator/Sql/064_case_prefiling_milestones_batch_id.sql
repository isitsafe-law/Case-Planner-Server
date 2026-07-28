SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Pre-filing sign-off/Settlement Authority final implementation, item 1: a bulk-mark action (the
-- Chief Counsel signs one pleadings package covering many tracts on the same job at once) still
-- writes one case_prefiling_milestones row per tract - this column links every row a single bulk
-- action touched so the audit trail can show they came from one action. Null for a single-case mark
-- from the case workspace, and cleared back to null whenever a milestone is unmarked (a correction
-- is no longer part of that batch's fact).

IF COL_LENGTH(N'$(Schema).case_prefiling_milestones', N'batch_id') IS NULL
    ALTER TABLE [$(Schema)].[case_prefiling_milestones] ADD [batch_id] nvarchar(64) NULL;
