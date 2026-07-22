SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Test-build feedback item: the Case Record's "Legal Assistant" field used to be derived live -
-- whichever Staff Directory legal assistant listed the case's Assigned Attorney in their
-- attorneyNames - shown as a single read-only value. That derivation could not represent two
-- attorneys on one case each needing their own legal assistant, nor a manual override (swapping an
-- LA between cases, or pairing one who doesn't normally work with that attorney). Converted to a
-- real, stored, one-to-many child table, mirroring case_opposing_attorneys
-- (031_case_opposing_attorneys.sql) column-for-column - same row_version/soft-delete shape. Unlike
-- opposing attorneys (free text), each row's name is chosen from the Staff Directory's active
-- legal assistants (038_staff_directory.sql) via a client-side dropdown, but is still stored here
-- as a plain name snapshot (not a foreign key to legal_assistants.id) so a name later deactivated
-- or removed from the directory stays intact on any case it was already saved to - the same
-- grandfathering convention already used for cases.assigned_attorney.

IF OBJECT_ID(N'$(Schema).case_legal_assistants','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[case_legal_assistants]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_case_legal_assistants] PRIMARY KEY,
        [case_id] bigint NOT NULL,
        [name] nvarchar(400) NOT NULL,
        [sort_order] int NOT NULL CONSTRAINT [DF_case_legal_assistants_sort_order] DEFAULT(0),
        [created_at] datetime2 NOT NULL CONSTRAINT [DF_case_legal_assistants_created] DEFAULT(SYSUTCDATETIME()),
        [updated_at] datetime2 NULL,
        [row_version] rowversion NOT NULL,
        [is_deleted] bit NOT NULL CONSTRAINT [DF_case_legal_assistants_is_deleted] DEFAULT(0),
        [deleted_utc] datetime2 NULL,
        [deleted_by_user_id] uniqueidentifier NULL,
        CONSTRAINT [FK_case_legal_assistants_cases] FOREIGN KEY ([case_id]) REFERENCES [$(Schema)].[cases] ([id]),
        CONSTRAINT [FK_case_legal_assistants_deleted_by_user] FOREIGN KEY ([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users] ([id])
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).case_legal_assistants') AND name=N'IX_case_legal_assistants_case_deleted')
    CREATE INDEX [IX_case_legal_assistants_case_deleted] ON [$(Schema)].[case_legal_assistants] ([case_id],[is_deleted]);
