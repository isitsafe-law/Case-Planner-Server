SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- dbo.case_assignments.assignment_role is pure access control (Owner/Collaborator/ReadOnly) and
-- says nothing about who is the attorney vs. the legal assistant on a case. This adds a second,
-- independent label - case_role (Attorney/LegalAssistant/Other) - alongside it. A case with a
-- second attorney is simply a second assignment row carrying case_role='Attorney'; nothing here
-- is auto-derived and there is no supervising-attorney hierarchy on app_users.

IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'$(Schema).case_assignments') AND name=N'case_role')
BEGIN
    ALTER TABLE [$(Schema)].[case_assignments] ADD [case_role] nvarchar(50) NOT NULL CONSTRAINT [DF_case_assignments_case_role] DEFAULT(N'Attorney');
END;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_case_assignments_case_role')
    ALTER TABLE [$(Schema)].[case_assignments] WITH CHECK ADD CONSTRAINT [CK_case_assignments_case_role]
        CHECK ([case_role] IN (N'Attorney',N'LegalAssistant',N'Other'));
