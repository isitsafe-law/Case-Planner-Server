SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Additive primary/supporting attorney relation. cases.assigned_attorney remains the
-- compatibility projection until all consumers are migrated.
IF OBJECT_ID(N'$(Schema).case_attorney_assignments','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[case_attorney_assignments]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_case_attorney_assignments] PRIMARY KEY,
        [case_id] bigint NOT NULL,
        [name] nvarchar(400) NOT NULL,
        [role] nvarchar(30) NOT NULL CONSTRAINT [DF_case_attorney_assignments_role] DEFAULT(N'Supporting'),
        [sort_order] int NOT NULL CONSTRAINT [DF_case_attorney_assignments_sort_order] DEFAULT(0),
        [created_at] datetime2 NOT NULL CONSTRAINT [DF_case_attorney_assignments_created] DEFAULT(SYSUTCDATETIME()),
        [updated_at] datetime2 NULL,
        [is_deleted] bit NOT NULL CONSTRAINT [DF_case_attorney_assignments_deleted] DEFAULT(0),
        CONSTRAINT [FK_case_attorney_assignments_cases] FOREIGN KEY ([case_id]) REFERENCES [$(Schema)].[cases]([id])
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).case_attorney_assignments') AND name=N'IX_case_attorney_assignments_case')
    CREATE INDEX [IX_case_attorney_assignments_case] ON [$(Schema)].[case_attorney_assignments]([case_id],[is_deleted],[sort_order]);
