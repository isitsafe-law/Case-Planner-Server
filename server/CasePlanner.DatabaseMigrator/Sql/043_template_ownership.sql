SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Template rewrite (ARDOT condemnation workflow content): the seed content in checklist_templates/
-- deadline_templates is being refreshed, and the existing reseed logic has a real gap - Seed*Async
-- either name-matches (checklist, safe but easy to get wrong) or unconditionally deletes-and-
-- reinserts (deadline, actively unsafe) on every version bump, with no way to tell "stock content
-- safe to refresh" apart from "a firm has hand-edited or created this, never touch it again".
-- is_custom is that durable marker: the seeding code path always writes/inserts with is_custom=0;
-- the Template Editor's save path (create, edit, or edit/delete of any child item) always flips the
-- PARENT template's is_custom to 1 the moment it's touched. A version-bump reseed then only ever
-- deletes/reinserts is_custom=0 rows, leaving anything a firm has touched permanently alone.
-- All existing rows predate this column entirely (100% seed-originated today), so the plain
-- DEFAULT(0) below is a correct backfill with no separate UPDATE needed.

IF COL_LENGTH(N'$(Schema).checklist_templates', N'is_custom') IS NULL
    ALTER TABLE [$(Schema)].[checklist_templates] ADD [is_custom] bit NOT NULL CONSTRAINT [DF_checklist_templates_is_custom] DEFAULT(0);

IF COL_LENGTH(N'$(Schema).deadline_templates', N'is_custom') IS NULL
    ALTER TABLE [$(Schema)].[deadline_templates] ADD [is_custom] bit NOT NULL CONSTRAINT [DF_deadline_templates_is_custom] DEFAULT(0);
