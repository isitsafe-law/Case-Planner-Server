SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Multiple defendants (often heirs to a property) can answer at genuinely different times - one
-- attorney appearing, or an answer being filed at all, typically signals the primary landowner
-- engaging on just compensation, while a distant heir who never responds shouldn't necessarily
-- read as the whole case being in default. Before this migration, "has an answer been filed" was
-- tracked as a single global cases.answer_filed/answer_filed_date pair (044_case_answer_filed.sql),
-- which cannot represent that. case_defendants is a real one-to-many child table, mirroring
-- case_legal_assistants (040_case_legal_assistants.sql) column-for-column - same row_version/
-- soft-delete/audit-trail shape - with additional per-defendant fields (address, service method/
-- date, and its own answer-filed fact/date) needed to track service and answer status
-- individually. address is deliberately kept as one free-text column rather than a nested
-- one-to-many address table - a defendant occasionally has more than one address, or a mix of an
-- address and a note, and that is fine to represent as plain text. service_method must support
-- "Warning Order" as a real value since Unknown Heirs entries are served that way with no
-- address. The prior case-level answer_filed/answer_filed_date columns are NOT dropped - they
-- stay as a dormant fallback for cases that predate this feature and have no defendant records
-- yet (see DefaultPostureCalculator.IsLikelyDefaultForDefendants). There is no live SQL Server
-- sandbox available here to exercise this against a real pilot instance - same limitation already
-- noted for every other migration file in this repo; this one has been reviewed for consistency
-- with its siblings but not executed live.

IF OBJECT_ID(N'$(Schema).case_defendants','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[case_defendants]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_case_defendants] PRIMARY KEY,
        [case_id] bigint NOT NULL,
        [name] nvarchar(400) NOT NULL,
        [address] nvarchar(max) NULL,
        [service_method] nvarchar(100) NULL,
        [served_date] nvarchar(20) NULL,
        [answer_filed] bit NOT NULL CONSTRAINT [DF_case_defendants_answer_filed] DEFAULT(0),
        [answer_filed_date] nvarchar(20) NULL,
        [notes] nvarchar(max) NULL,
        [sort_order] int NOT NULL CONSTRAINT [DF_case_defendants_sort_order] DEFAULT(0),
        [created_at] datetime2 NOT NULL CONSTRAINT [DF_case_defendants_created] DEFAULT(SYSUTCDATETIME()),
        [updated_at] datetime2 NULL,
        [row_version] rowversion NOT NULL,
        [is_deleted] bit NOT NULL CONSTRAINT [DF_case_defendants_is_deleted] DEFAULT(0),
        [deleted_utc] datetime2 NULL,
        [deleted_by_user_id] uniqueidentifier NULL,
        CONSTRAINT [FK_case_defendants_cases] FOREIGN KEY ([case_id]) REFERENCES [$(Schema)].[cases] ([id]),
        CONSTRAINT [FK_case_defendants_deleted_by_user] FOREIGN KEY ([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users] ([id])
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).case_defendants') AND name=N'IX_case_defendants_case_deleted')
    CREATE INDEX [IX_case_defendants_case_deleted] ON [$(Schema)].[case_defendants] ([case_id],[is_deleted]);
