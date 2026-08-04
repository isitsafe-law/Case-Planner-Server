SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Distinct from marked_by_display/marked_by_role (who acted in the system, e.g. an assistant): the
-- free-text name/role of the real approving party when a milestone represents someone else's
-- sign-off (e.g. Chief Counsel's signature, marked by the assistant on her behalf). Null when the
-- acting user IS the approving party (most milestones), or simply not recorded. Cleared back to
-- null whenever a milestone is unmarked, same convention as batch_id.

IF COL_LENGTH(N'$(Schema).case_prefiling_milestones', N'on_behalf_of_display') IS NULL
    ALTER TABLE [$(Schema)].[case_prefiling_milestones] ADD [on_behalf_of_display] nvarchar(200) NULL;

IF COL_LENGTH(N'$(Schema).case_prefiling_milestones', N'on_behalf_of_role') IS NULL
    ALTER TABLE [$(Schema)].[case_prefiling_milestones] ADD [on_behalf_of_role] nvarchar(100) NULL;
