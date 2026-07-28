SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Pre-filing sign-off/Settlement Authority final implementation, item 4: distinguishes a
-- historically-imported case (no real in-system Director-signature event to ever record) from one
-- that originated in this system, so PipelinePromotionGate.RequiresFilingApproval can skip the
-- Director-signature forcing-prompt entirely for the former rather than just softening it. Defaults
-- to 1 (true) - the DEFAULT constraint backfills every existing row, since nothing that already
-- exists should suddenly be treated as imported. Only the CSV/Excel import services
-- (SqlServerCaseImportService and its SQLite equivalent) ever write 0, and only at the moment a
-- brand-new row is inserted; every later save (SqlServerCaseCatalogReader.SaveCaseAsync) excludes
-- this column from its UPDATE entirely, making it immutable after creation by construction - the
-- same convention created_at already uses in that method.

IF COL_LENGTH(N'$(Schema).cases', N'originated_in_system') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [originated_in_system] bit NOT NULL CONSTRAINT [DF_cases_originated_in_system] DEFAULT(1);
