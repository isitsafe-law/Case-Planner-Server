SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Manager/Administrator Dashboard Milestone 4 correction: replaces part of Milestone 2's Filing
-- Approval gate (pipeline_holder_approvals, holder_role='Chief Counsel') with a plain record of
-- ARDOT's real, out-of-band pre-filing sign-off process. That process happens outside this system
-- entirely, by email: the pleadings package (Complaint in Condemnation, Declaration of Taking, and
-- other documents varying by case) is emailed to the Chief Counsel, who signs and emails it back;
-- the Declaration of Taking then goes separately to the Director of Highways and Transportation for
-- signature - the Director is NOT a user of this system and never logs in. This table does not
-- gate, route, or collect those approvals - it RECORDS that the out-of-band sign-offs occurred, so
-- the division can see what each Pipeline tract is waiting on.
--
-- One row per (case_id, milestone), upserted in place - created on first mark, updated on every
-- subsequent mark/unmark of that same milestone. Unlike pipeline_holder_approvals
-- (052_pipeline_holder_approvals.sql, append-only), this mirrors settlement_authority_requests's
-- (058_settlement_authority_requests.sql) "updates in place" shape. milestone's four allowed values
-- (PleadingsPackageSent/ChiefCounselSignaturesReceived/DeclarationOfTakingSentToDirector/
-- DirectorSignatureReceived) are enforced in C# (PreFilingMilestoneGate), not a DB constraint,
-- matching manager_tier's/cases.case_status's existing convention. occurred_date is the
-- user-entered date the signature/action actually occurred (null while is_marked=0, and
-- deliberately unvalidated against "today or earlier" - it will often be backdated); marked_at is
-- automatic, stamped on whichever mark/unmark action most recently changed the row. note is a
-- plain free-text field - on PleadingsPackageSent specifically, the client may format it as a
-- simple checklist-style list since package contents vary by case, but that is a client-side UX
-- choice only; there is deliberately no separate structured document-checklist table here. There
-- is no live SQL Server sandbox available here to exercise this against a real pilot instance -
-- same caveat already noted for every other migration file in this repo; this one has been
-- reviewed for consistency with its siblings but not executed live.

IF OBJECT_ID(N'$(Schema).case_prefiling_milestones','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[case_prefiling_milestones]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_case_prefiling_milestones] PRIMARY KEY,
        [case_id] bigint NOT NULL,
        [milestone] nvarchar(60) NOT NULL,
        [is_marked] bit NOT NULL CONSTRAINT [DF_case_prefiling_milestones_is_marked] DEFAULT(0),
        [occurred_date] nvarchar(20) NULL,
        [marked_at] nvarchar(40) NULL,
        [marked_by_user_id] nvarchar(100) NULL,
        [marked_by_display] nvarchar(400) NULL,
        [marked_by_role] nvarchar(100) NULL,
        [note] nvarchar(max) NULL,
        [row_version] rowversion NOT NULL,
        CONSTRAINT [FK_case_prefiling_milestones_cases] FOREIGN KEY ([case_id]) REFERENCES [$(Schema)].[cases] ([id]),
        CONSTRAINT [UQ_case_prefiling_milestones_case_milestone] UNIQUE ([case_id],[milestone])
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).case_prefiling_milestones') AND name=N'IX_case_prefiling_milestones_case_id')
    CREATE INDEX [IX_case_prefiling_milestones_case_id] ON [$(Schema)].[case_prefiling_milestones] ([case_id]);
