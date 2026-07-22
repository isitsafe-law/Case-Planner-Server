SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Test-build feedback batch (task assignment): checklist_items.assigned_user_id
-- (032_checklist_item_assignment.sql) is FK'd to dbo.app_users and only ever meaningfully
-- populated/selectable once Entra is enabled - it stays exactly as-is, dormant, along with its
-- TaskAssigned notification trigger. This adds a second, separate assignee column that actually
-- drives the UI today: a plain name snapshot (no FK), sourced from the case's Assigned Attorney
-- and Legal Assistants, following the same "opaque name" convention already used for
-- cases.assigned_attorney and case_legal_assistants.name - a name stays intact on a task even if
-- later removed from the Staff Directory.

IF COL_LENGTH(N'$(Schema).checklist_items', N'assigned_staff_name') IS NULL
    ALTER TABLE [$(Schema)].[checklist_items] ADD [assigned_staff_name] nvarchar(400) NULL;
