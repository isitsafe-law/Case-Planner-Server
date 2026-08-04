SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Legal Assistant view, phase 2: "Attorney" | "LegalAssistant" | "Either" (default) - which role a
-- task is naturally for, distinct from assigned_staff_name (who among possibly-several people of
-- that role currently owns it). Filters which dashboard's queue shows a task; every existing row
-- defaults to "Either" so nothing already on either dashboard disappears - classifying specific
-- tasks/templates more precisely is a deliberate later pass, not guessed here from task text.

IF COL_LENGTH(N'$(Schema).checklist_items', N'owner_role') IS NULL
    ALTER TABLE [$(Schema)].[checklist_items] ADD [owner_role] nvarchar(20) NOT NULL CONSTRAINT [DF_checklist_items_owner_role] DEFAULT('Either');
