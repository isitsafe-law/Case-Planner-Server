SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- ROW intake tracking: "Received from ROW" | "In Title Review" | "Returned to ROW" |
-- "Ready for Assignment" | "Acquired by Agreement" | "Project Revised" | "Withdrawn". A different
-- axis from current_holder/pipeline_stage (who currently holds the file in the internal Legal
-- Assistant -> Attorney -> Deputy Chief Counsel -> Chief Counsel review chain) - this tracks where
-- a tract sits relative to ROW, which happens earlier. Null for any case that was never tracked
-- through ROW intake. Deliberately a separate column from case_status (which stays "Pipeline" the
-- whole pre-filing lifecycle) and from disposition_type (litigation-outcome-scoped, not reused
-- here). See CasePlanner.Web.Server/Models/DomainModels.cs's CaseRecord.RowIntakeStatus.

IF COL_LENGTH(N'$(Schema).cases', N'row_intake_status') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [row_intake_status] nvarchar(40) NULL;
