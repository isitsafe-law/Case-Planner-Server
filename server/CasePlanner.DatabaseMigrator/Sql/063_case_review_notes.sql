SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Pre-filing sign-off/Settlement Authority final implementation, item 2: an unstructured review-note
-- log for a Pipeline tract, deliberately separate in shape from case_prefiling_milestones
-- (060_case_prefiling_milestones.sql) - no fixed order, no required participant, no requirement
-- that one exist before/after any milestone. Models the real norm that a second set of eyes on
-- initial pleadings (often Deputy Chief Counsel, but not always, and not gated) is a strong
-- practice, not a workflow step to enforce. reviewer_name/reviewer_role are free text - the
-- reviewer is not necessarily a fixed role or even a system user. decision is a short,
-- lightly-constrained free-text field, not an enum tied to a workflow state - the client offers a
-- few common values (e.g. "Looks good", "Sent back for revision") plus free text, but nothing here
-- enforces a fixed vocabulary. Append-only like pipeline_holder_approvals
-- (052_pipeline_holder_approvals.sql) - no update, no delete, since a review note is never edited or
-- retracted once entered; created_at/created_by_* are the system-entry facts, distinct from
-- occurred_date (the user-entered, editable/backdatable date of the review itself) and from
-- reviewer_name/reviewer_role (who actually did the review). Feeds stall detection (a review note
-- with decision "sent back for revision" more recent than the last milestone mark switches the
-- aging clock/label) but enforces nothing on its own.

IF OBJECT_ID(N'$(Schema).case_review_notes','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[case_review_notes]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_case_review_notes] PRIMARY KEY,
        [case_id] bigint NOT NULL,
        [reviewer_name] nvarchar(400) NULL,
        [reviewer_role] nvarchar(100) NULL,
        [decision] nvarchar(200) NOT NULL,
        [comment] nvarchar(max) NULL,
        [occurred_date] nvarchar(20) NOT NULL,
        [created_at] nvarchar(40) NOT NULL,
        [created_by_user_id] nvarchar(100) NULL,
        [created_by_display] nvarchar(400) NULL,
        [created_by_role] nvarchar(100) NULL,
        CONSTRAINT [FK_case_review_notes_cases] FOREIGN KEY ([case_id]) REFERENCES [$(Schema)].[cases] ([id])
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).case_review_notes') AND name=N'IX_case_review_notes_case_id')
    CREATE INDEX [IX_case_review_notes_case_id] ON [$(Schema)].[case_review_notes] ([case_id]);
