SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Manager Dashboard sign-off consolidation, item 4: Settlement Authority becomes pure
-- record-keeping. Before this, decided_at/decided_by_display/decided_by_role only captured when
-- and by whom the SYSTEM ENTRY was made ("recorded") - there was no way to separately record who
-- actually GRANTED the authority (often outside the system entirely, e.g. Chief Counsel approving
-- verbally or by email, with a Legal Assistant entering it here later) or the real-world date that
-- happened, distinct from decided_at. granted_by/granted_by_role/granted_date fill that gap for the
-- Approved outcome specifically; document_reference is an optional pointer to supporting
-- correspondence/paperwork, usable for any outcome. None of these are validated against
-- app_users/manager_tier - they are free text, matching requesting_attorney's existing convention,
-- since a real-world grant may not correspond to any system user at all.

IF COL_LENGTH(N'$(Schema).settlement_authority_requests', N'granted_by') IS NULL
    ALTER TABLE [$(Schema)].[settlement_authority_requests] ADD [granted_by] nvarchar(400) NULL;

IF COL_LENGTH(N'$(Schema).settlement_authority_requests', N'granted_by_role') IS NULL
    ALTER TABLE [$(Schema)].[settlement_authority_requests] ADD [granted_by_role] nvarchar(100) NULL;

IF COL_LENGTH(N'$(Schema).settlement_authority_requests', N'granted_date') IS NULL
    ALTER TABLE [$(Schema)].[settlement_authority_requests] ADD [granted_date] nvarchar(40) NULL;

IF COL_LENGTH(N'$(Schema).settlement_authority_requests', N'document_reference') IS NULL
    ALTER TABLE [$(Schema)].[settlement_authority_requests] ADD [document_reference] nvarchar(500) NULL;
