IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_case_assignments_role')
    ALTER TABLE [$(Schema)].[case_assignments] WITH CHECK ADD CONSTRAINT [CK_case_assignments_role]
        CHECK ([assignment_role] IN (N'Owner',N'Collaborator',N'ReadOnly'));

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).case_assignments') AND name=N'IX_case_assignments_case_role')
    CREATE INDEX [IX_case_assignments_case_role] ON [$(Schema)].[case_assignments] ([case_id],[assignment_role]);
