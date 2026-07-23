SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Pre-suit intake gate (office pilot): the Legal Assistant -> Attorney -> Deputy Chief Counsel ->
-- Chief Counsel chain, tracked today only via cases.current_holder (a plain string) and
-- pipeline_handoffs (an append-only history log with no per-holder status concept). The office
-- wants a real gate - a holder cannot advance the case to the next role until they have
-- explicitly marked their own review "Approved" - which requires a place to record that fact.
-- pipeline_holder_approvals is that place: a real one-to-many child table, mirroring
-- case_defendants (045_case_defendants.sql) in dual-provider shape, but append-only rather than
-- updatable - every Approve/Return-for-Revision action inserts a NEW row (never updates an
-- existing one) so history survives a cycle like Approved -> Returned -> re-Approved. "Pending" is
-- never itself stored as a row - the absence of any row yet for a (case_id, holder_role) pair
-- already means pending. set_by_display_name is deliberately plain free text with no FK to
-- app_users - there is no live authentication yet (Entra ID is dormant), so this enforces PROCESS
-- (you can't advance without clicking Approve), not IDENTITY - the same limitation
-- pipeline_handoffs.created_by_display already carries. There is no live SQL Server sandbox
-- available here to exercise this against a real pilot instance - same caveat already noted for
-- every other migration file in this repo; this one has been reviewed for consistency with its
-- siblings but not executed live.

IF OBJECT_ID(N'$(Schema).pipeline_holder_approvals','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[pipeline_holder_approvals]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_pipeline_holder_approvals] PRIMARY KEY,
        [case_id] bigint NOT NULL,
        [holder_role] nvarchar(100) NOT NULL,
        [status] nvarchar(20) NOT NULL,
        [note] nvarchar(max) NULL,
        [set_at] nvarchar(40) NOT NULL,
        [set_by_display_name] nvarchar(400) NULL,
        [row_version] rowversion NOT NULL,
        CONSTRAINT [FK_pipeline_holder_approvals_cases] FOREIGN KEY ([case_id]) REFERENCES [$(Schema)].[cases] ([id])
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).pipeline_holder_approvals') AND name=N'IX_pipeline_holder_approvals_case_role')
    CREATE INDEX [IX_pipeline_holder_approvals_case_role] ON [$(Schema)].[pipeline_holder_approvals] ([case_id],[holder_role],[id]);
