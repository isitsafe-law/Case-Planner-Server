SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

/* Additive metadata for the unified document catalog.  Existing template rows
   are preserved and receive safe defaults; administrators can refine them in
   the catalog after cutover. */
IF COL_LENGTH(N'$(Schema).custom_document_templates',N'description') IS NULL
    ALTER TABLE [$(Schema)].[custom_document_templates] ADD [description] nvarchar(1000) NOT NULL CONSTRAINT [DF_custom_document_templates_description] DEFAULT(N'');
IF COL_LENGTH(N'$(Schema).custom_document_templates',N'category') IS NULL
    ALTER TABLE [$(Schema)].[custom_document_templates] ADD [category] nvarchar(80) NOT NULL CONSTRAINT [DF_custom_document_templates_category] DEFAULT(N'Other');
IF COL_LENGTH(N'$(Schema).custom_document_templates',N'visibility') IS NULL
    ALTER TABLE [$(Schema)].[custom_document_templates] ADD [visibility] nvarchar(20) NOT NULL CONSTRAINT [DF_custom_document_templates_visibility] DEFAULT(N'Personal');
IF COL_LENGTH(N'$(Schema).custom_document_templates',N'default_output_file_name') IS NULL
    ALTER TABLE [$(Schema)].[custom_document_templates] ADD [default_output_file_name] nvarchar(260) NULL;
IF COL_LENGTH(N'$(Schema).custom_document_templates',N'recommended_case_type') IS NULL
    ALTER TABLE [$(Schema)].[custom_document_templates] ADD [recommended_case_type] nvarchar(120) NULL;

IF OBJECT_ID(N'$(Schema).document_tags','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[document_tags]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_document_tags] PRIMARY KEY,
        [tag_key] nvarchar(120) NOT NULL,
        [display_name] nvarchar(200) NOT NULL,
        [scope] nvarchar(20) NOT NULL CONSTRAINT [DF_document_tags_scope] DEFAULT(N'Global'),
        [owner_user_id] uniqueidentifier NULL,
        [is_active] bit NOT NULL CONSTRAINT [DF_document_tags_active] DEFAULT(1),
        CONSTRAINT [FK_document_tags_owner] FOREIGN KEY([owner_user_id]) REFERENCES [$(Schema)].[app_users]([id])
    );
    CREATE UNIQUE INDEX [UX_document_tags_key_owner] ON [$(Schema)].[document_tags]([tag_key],[owner_user_id]);
END;

IF OBJECT_ID(N'$(Schema).document_template_tag_links','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[document_template_tag_links]
    (
        [template_id] bigint NOT NULL,
        [tag_id] bigint NOT NULL,
        CONSTRAINT [PK_document_template_tag_links] PRIMARY KEY([template_id],[tag_id]),
        CONSTRAINT [FK_document_template_tag_links_template] FOREIGN KEY([template_id]) REFERENCES [$(Schema)].[custom_document_templates]([id]),
        CONSTRAINT [FK_document_template_tag_links_tag] FOREIGN KEY([tag_id]) REFERENCES [$(Schema)].[document_tags]([id])
    );
END;

IF OBJECT_ID(N'$(Schema).discovery_item_tag_links','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[discovery_item_tag_links]
    (
        [discovery_item_id] bigint NOT NULL,
        [tag_id] bigint NOT NULL,
        CONSTRAINT [PK_discovery_item_tag_links] PRIMARY KEY([discovery_item_id],[tag_id]),
        CONSTRAINT [FK_discovery_item_tag_links_item] FOREIGN KEY([discovery_item_id]) REFERENCES [$(Schema)].[discovery_template_items]([id]),
        CONSTRAINT [FK_discovery_item_tag_links_tag] FOREIGN KEY([tag_id]) REFERENCES [$(Schema)].[document_tags]([id])
    );
END;
