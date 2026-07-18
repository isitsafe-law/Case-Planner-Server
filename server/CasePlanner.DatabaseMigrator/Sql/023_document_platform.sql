SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Build-plan step 3 (data model cutover): the unified document platform. Replaces
-- custom_document_templates, discovery_base_versions, discovery_template_items,
-- document_exports, and discovery_generations as the long-term document/template/generation
-- schema - see docs/document-system-audit-and-plan (Architecture) for the full design and
-- docs/sql-server-migration.md for the cutover note. Existing-data migration into these tables
-- is a separate follow-up; this script only creates the schema.

-- issue_tags predates this cutover and never had a real uniqueness guarantee on name - the
-- SQLite side only ever checked "not exists" before inserting the fixed seed catalog. The new
-- create-tag endpoint needs an actual constraint, not just an application-level check, since two
-- concurrent creates on a multi-user server could otherwise both pass a COUNT check and insert
-- the same name twice.
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).issue_tags') AND name=N'UX_issue_tags_name')
    CREATE UNIQUE INDEX [UX_issue_tags_name] ON [$(Schema)].[issue_tags]([name]);

IF OBJECT_ID(N'$(Schema).document_templates','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[document_templates]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_document_templates] PRIMARY KEY,
        [template_key] nvarchar(260) NOT NULL,
        [title] nvarchar(400) NOT NULL,
        [description] nvarchar(1000) NULL,
        [category] nvarchar(100) NOT NULL CONSTRAINT [DF_document_templates_category] DEFAULT(N'Other'),
        [document_type] nvarchar(100) NULL,
        [is_builtin] bit NOT NULL CONSTRAINT [DF_document_templates_builtin] DEFAULT(0),
        [is_deleted] bit NOT NULL CONSTRAINT [DF_document_templates_deleted] DEFAULT(0),
        [created_utc] datetime2 NOT NULL CONSTRAINT [DF_document_templates_created] DEFAULT(SYSUTCDATETIME()),
        [created_by_user_id] uniqueidentifier NULL,
        [row_version] rowversion NOT NULL,
        CONSTRAINT [FK_document_templates_creator] FOREIGN KEY([created_by_user_id]) REFERENCES [$(Schema)].[app_users]([id])
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).document_templates') AND name=N'UX_document_templates_key')
    CREATE UNIQUE INDEX [UX_document_templates_key] ON [$(Schema)].[document_templates]([template_key]);

IF OBJECT_ID(N'$(Schema).document_template_versions','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[document_template_versions]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_document_template_versions] PRIMARY KEY,
        [template_id] bigint NOT NULL,
        [version] int NOT NULL,
        [storage_path] nvarchar(2048) NOT NULL,
        [tokens_json] nvarchar(max) NOT NULL CONSTRAINT [DF_document_template_versions_tokens] DEFAULT(N'[]'),
        [unknown_tokens_json] nvarchar(max) NOT NULL CONSTRAINT [DF_document_template_versions_unknown_tokens] DEFAULT(N'[]'),
        [is_active] bit NOT NULL CONSTRAINT [DF_document_template_versions_active] DEFAULT(0),
        [created_utc] datetime2 NOT NULL CONSTRAINT [DF_document_template_versions_created] DEFAULT(SYSUTCDATETIME()),
        [created_by_user_id] uniqueidentifier NULL,
        [row_version] rowversion NOT NULL,
        CONSTRAINT [FK_document_template_versions_template] FOREIGN KEY([template_id]) REFERENCES [$(Schema)].[document_templates]([id]),
        CONSTRAINT [FK_document_template_versions_creator] FOREIGN KEY([created_by_user_id]) REFERENCES [$(Schema)].[app_users]([id])
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).document_template_versions') AND name=N'UX_document_template_versions_template_version')
    CREATE UNIQUE INDEX [UX_document_template_versions_template_version] ON [$(Schema)].[document_template_versions]([template_id],[version]);
-- One active version per template - the same filtered-unique-index pattern custom_document_templates
-- already uses (015_custom_document_templates.sql) to make "which version is live" unambiguous.
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).document_template_versions') AND name=N'UX_document_template_versions_active')
    CREATE UNIQUE INDEX [UX_document_template_versions_active] ON [$(Schema)].[document_template_versions]([template_id]) WHERE [is_active]=1;

IF OBJECT_ID(N'$(Schema).document_runtime_inputs','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[document_runtime_inputs]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_document_runtime_inputs] PRIMARY KEY,
        [template_version_id] bigint NOT NULL,
        [field_key] nvarchar(200) NOT NULL,
        [label] nvarchar(400) NOT NULL,
        [field_type] nvarchar(20) NOT NULL CONSTRAINT [DF_document_runtime_inputs_type] DEFAULT(N'text'),
        [is_required] bit NOT NULL CONSTRAINT [DF_document_runtime_inputs_required] DEFAULT(1),
        [sort_order] int NOT NULL CONSTRAINT [DF_document_runtime_inputs_sort] DEFAULT(0),
        CONSTRAINT [FK_document_runtime_inputs_version] FOREIGN KEY([template_version_id]) REFERENCES [$(Schema)].[document_template_versions]([id])
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).document_runtime_inputs') AND name=N'IX_document_runtime_inputs_version')
    CREATE INDEX [IX_document_runtime_inputs_version] ON [$(Schema)].[document_runtime_inputs]([template_version_id]);

IF OBJECT_ID(N'$(Schema).document_template_sections','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[document_template_sections]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_document_template_sections] PRIMARY KEY,
        [template_version_id] bigint NOT NULL,
        [section_key] nvarchar(200) NOT NULL,
        [label] nvarchar(400) NOT NULL,
        [description] nvarchar(1000) NULL,
        [issue_tag_name] nvarchar(200) NULL,
        [sort_order] int NOT NULL CONSTRAINT [DF_document_template_sections_sort] DEFAULT(0),
        CONSTRAINT [FK_document_template_sections_version] FOREIGN KEY([template_version_id]) REFERENCES [$(Schema)].[document_template_versions]([id])
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).document_template_sections') AND name=N'UX_document_template_sections_version_key')
    CREATE UNIQUE INDEX [UX_document_template_sections_version_key] ON [$(Schema)].[document_template_sections]([template_version_id],[section_key]);

-- section_a_id/section_b_id: the pair is unordered (A overlaps B is the same fact as B overlaps
-- A). The CHECK forces callers to store the smaller id first, so the unique index actually
-- catches a reversed duplicate insert instead of silently allowing it - matches the SQLite shape
-- in CasePlannerRepository.DocumentPlatform.cs exactly.
IF OBJECT_ID(N'$(Schema).document_section_overlaps','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[document_section_overlaps]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_document_section_overlaps] PRIMARY KEY,
        [section_a_id] bigint NOT NULL,
        [section_b_id] bigint NOT NULL,
        [note] nvarchar(1000) NULL,
        CONSTRAINT [FK_document_section_overlaps_a] FOREIGN KEY([section_a_id]) REFERENCES [$(Schema)].[document_template_sections]([id]),
        CONSTRAINT [FK_document_section_overlaps_b] FOREIGN KEY([section_b_id]) REFERENCES [$(Schema)].[document_template_sections]([id]),
        CONSTRAINT [CK_document_section_overlaps_order] CHECK([section_a_id] < [section_b_id])
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).document_section_overlaps') AND name=N'UX_document_section_overlaps_pair')
    CREATE UNIQUE INDEX [UX_document_section_overlaps_pair] ON [$(Schema)].[document_section_overlaps]([section_a_id],[section_b_id]);

IF OBJECT_ID(N'$(Schema).document_generations','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[document_generations]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_document_generations] PRIMARY KEY,
        [case_id] bigint NOT NULL,
        [template_id] bigint NOT NULL,
        [template_version_id] bigint NOT NULL,
        [output_path] nvarchar(2048) NOT NULL,
        [rendered_utc] datetime2 NOT NULL CONSTRAINT [DF_document_generations_rendered] DEFAULT(SYSUTCDATETIME()),
        [generated_by_user_id] uniqueidentifier NULL,
        [sections_included_json] nvarchar(max) NOT NULL CONSTRAINT [DF_document_generations_sections] DEFAULT(N'[]'),
        [runtime_input_values_json] nvarchar(max) NOT NULL CONSTRAINT [DF_document_generations_inputs] DEFAULT(N'{}'),
        [is_draft] bit NOT NULL CONSTRAINT [DF_document_generations_draft] DEFAULT(1),
        [is_finalized] bit NOT NULL CONSTRAINT [DF_document_generations_finalized] DEFAULT(0),
        [missing_fields_json] nvarchar(max) NOT NULL CONSTRAINT [DF_document_generations_missing] DEFAULT(N'[]'),
        CONSTRAINT [FK_document_generations_case] FOREIGN KEY([case_id]) REFERENCES [$(Schema)].[cases]([id]),
        CONSTRAINT [FK_document_generations_template] FOREIGN KEY([template_id]) REFERENCES [$(Schema)].[document_templates]([id]),
        CONSTRAINT [FK_document_generations_version] FOREIGN KEY([template_version_id]) REFERENCES [$(Schema)].[document_template_versions]([id]),
        CONSTRAINT [FK_document_generations_actor] FOREIGN KEY([generated_by_user_id]) REFERENCES [$(Schema)].[app_users]([id])
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).document_generations') AND name=N'IX_document_generations_case')
    CREATE INDEX [IX_document_generations_case] ON [$(Schema)].[document_generations]([case_id]);
