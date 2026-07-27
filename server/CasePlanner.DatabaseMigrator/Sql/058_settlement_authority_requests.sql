SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Manager/Administrator Dashboard Milestone 3: the Settlement Authority workflow - a request for
-- authority to settle up to a given amount, decided EXCLUSIVELY by Chief Counsel (no amount
-- threshold, no Deputy Chief Counsel action rights, and deliberately no Administrator override -
-- already decided with the user, and stricter than every other admin-gated action in this app;
-- see manager_tier's routing note in 056_manager_tier.sql for the parallel Filing Approval
-- decision). Before this, SettlementAuthorityRequested/SettlementAuthorityReceived existed only as
-- free-text activity_log.activity_type labels with nothing backing them - this table makes the
-- workflow real for the first time and feeds cases.settlement_authorized_ceiling
-- (059_case_settlement_authorized_ceiling.sql).
--
-- Unlike pipeline_holder_approvals (052_pipeline_holder_approvals.sql), this is NOT append-only -
-- it updates in place, one row per request, the same way discovery_postures does: only one open
-- thread (status Pending/InfoRequested) may exist per case at a time, enforced in C#
-- (ISettlementAuthorityRequestStore.CreateAsync), not a DB constraint. The full decision history
-- (who decided what, when, with what comment) lives in activity_log via the diff columns added in
-- 057_activity_log_role_and_diff.sql (FieldChanged/PreviousValue/NewValue) - this row only ever
-- carries the current/most-recent state. status's allowed values (Pending/Approved/Denied/
-- InfoRequested) are enforced in C#, not a DB CHECK constraint, matching manager_tier's/
-- cases.case_status's existing convention. There is no live SQL Server sandbox available here to
-- exercise this against a real pilot instance - same caveat already noted for every other
-- migration file in this repo; this one has been reviewed for consistency with its siblings but
-- not executed live.

IF OBJECT_ID(N'$(Schema).settlement_authority_requests','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[settlement_authority_requests]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_settlement_authority_requests] PRIMARY KEY,
        [case_id] bigint NOT NULL,
        [requested_amount] decimal(18,2) NOT NULL,
        [requesting_attorney] nvarchar(200) NULL,
        [request_notes] nvarchar(max) NULL,
        [status] nvarchar(20) NOT NULL CONSTRAINT [DF_settlement_authority_requests_status] DEFAULT('Pending'),
        [granted_amount] decimal(18,2) NULL,
        [requested_at] nvarchar(40) NOT NULL,
        [requested_by_user_id] nvarchar(100) NULL,
        [requested_by_display] nvarchar(400) NULL,
        [decided_at] nvarchar(40) NULL,
        [decided_by_user_id] nvarchar(100) NULL,
        [decided_by_display] nvarchar(400) NULL,
        [decided_by_role] nvarchar(100) NULL,
        [decision_comment] nvarchar(max) NULL,
        [row_version] rowversion NOT NULL,
        CONSTRAINT [FK_settlement_authority_requests_cases] FOREIGN KEY ([case_id]) REFERENCES [$(Schema)].[cases] ([id])
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).settlement_authority_requests') AND name=N'IX_settlement_authority_requests_case_id')
    CREATE INDEX [IX_settlement_authority_requests_case_id] ON [$(Schema)].[settlement_authority_requests] ([case_id],[id]);
