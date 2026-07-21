SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Multi-user rollout Phase 5 (reporting) prerequisite: Staff Directory. A fixed list of real
-- attorney/legal-assistant names for case metadata and reporting - deliberately separate from
-- dbo.app_users (the Entra-provisioned, sign-in roster). Zero auth/identity dependency, so like
-- 037_case_disposition_fields.sql this gets full, normal dual-provider parity; there is no live
-- SQL Server sandbox available here to exercise this against a real pilot instance - same
-- limitation already noted for every other migration in this repo - so this file has been
-- reviewed for consistency with its siblings (idempotent CREATE TABLE + IF NOT EXISTS seed,
-- matching 016_organization_defaults.sql) but not executed live.

IF OBJECT_ID(N'$(Schema).attorneys','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[attorneys]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_attorneys] PRIMARY KEY,
        [name] nvarchar(400) NOT NULL,
        [title] nvarchar(200) NULL,
        [is_active] bit NOT NULL CONSTRAINT [DF_attorneys_is_active] DEFAULT(1),
        [sort_order] int NOT NULL CONSTRAINT [DF_attorneys_sort_order] DEFAULT(0)
    );
END;

IF OBJECT_ID(N'$(Schema).legal_assistants','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[legal_assistants]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_legal_assistants] PRIMARY KEY,
        [name] nvarchar(400) NOT NULL,
        [is_active] bit NOT NULL CONSTRAINT [DF_legal_assistants_is_active] DEFAULT(1),
        [sort_order] int NOT NULL CONSTRAINT [DF_legal_assistants_sort_order] DEFAULT(0)
    );
END;

IF OBJECT_ID(N'$(Schema).legal_assistant_attorneys','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[legal_assistant_attorneys]
    (
        [legal_assistant_id] bigint NOT NULL,
        [attorney_id] bigint NOT NULL,
        CONSTRAINT [PK_legal_assistant_attorneys] PRIMARY KEY([legal_assistant_id],[attorney_id]),
        CONSTRAINT [FK_laa_legal_assistant] FOREIGN KEY([legal_assistant_id]) REFERENCES [$(Schema)].[legal_assistants]([id]),
        CONSTRAINT [FK_laa_attorney] FOREIGN KEY([attorney_id]) REFERENCES [$(Schema)].[attorneys]([id])
    );
END;

-- Idempotent seed - only runs the first time (mirrors the "only seed if the attorneys table is
-- empty" rule used by CasePlannerRepository.SeedStaffDirectoryAsync on the SQLite side), so this
-- never re-runs or duplicates on subsequent deployments and never clobbers office edits made
-- after the initial seed.
IF NOT EXISTS(SELECT 1 FROM [$(Schema)].[attorneys])
BEGIN
    DECLARE @attorneys TABLE ([name] nvarchar(400), [title] nvarchar(200), [sort_order] int);
    INSERT INTO @attorneys ([name],[title],[sort_order]) VALUES
        (N'Michelle Davenport', N'Chief Counsel', 1),
        (N'Angela Dodson', N'Deputy Chief Counsel', 2),
        (N'Helen Newberry', NULL, 3),
        (N'Stephen Lowman', NULL, 4),
        (N'Cody Eenigenburg', NULL, 5),
        (N'Iván Martínez', NULL, 6),
        (N'Katie Meister', NULL, 7),
        (N'Michael Bynum', NULL, 8),
        (N'Bailey Gambill', NULL, 9);

    INSERT INTO [$(Schema)].[attorneys] ([name],[title],[is_active],[sort_order])
    SELECT [name],[title],1,[sort_order] FROM @attorneys;

    DECLARE @legalAssistants TABLE ([name] nvarchar(400), [sort_order] int);
    INSERT INTO @legalAssistants ([name],[sort_order]) VALUES
        (N'Tyler Story', 1),
        (N'Evelyn Allison', 2),
        (N'Donna Ramsey', 3);

    INSERT INTO [$(Schema)].[legal_assistants] ([name],[is_active],[sort_order])
    SELECT [name],1,[sort_order] FROM @legalAssistants;

    DECLARE @ties TABLE ([legal_assistant_name] nvarchar(400), [attorney_name] nvarchar(400));
    INSERT INTO @ties ([legal_assistant_name],[attorney_name]) VALUES
        (N'Tyler Story', N'Stephen Lowman'),
        (N'Tyler Story', N'Cody Eenigenburg'),
        (N'Evelyn Allison', N'Michael Bynum'),
        (N'Evelyn Allison', N'Helen Newberry'),
        (N'Evelyn Allison', N'Bailey Gambill'),
        (N'Donna Ramsey', N'Iván Martínez'),
        (N'Donna Ramsey', N'Katie Meister');

    INSERT INTO [$(Schema)].[legal_assistant_attorneys] ([legal_assistant_id],[attorney_id])
    SELECT la.[id], a.[id]
    FROM @ties t
    JOIN [$(Schema)].[legal_assistants] la ON la.[name] = t.[legal_assistant_name]
    JOIN [$(Schema)].[attorneys] a ON a.[name] = t.[attorney_name];
END;
