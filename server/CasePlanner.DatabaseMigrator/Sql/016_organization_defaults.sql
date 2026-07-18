SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF OBJECT_ID(N'$(Schema).organization_defaults','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[organization_defaults]
    (
        [id] bigint NOT NULL CONSTRAINT [PK_organization_defaults] PRIMARY KEY,
        [attorney_name] nvarchar(400) NOT NULL CONSTRAINT [DF_org_defaults_attorney_name] DEFAULT(N''),
        [bar_number] nvarchar(100) NOT NULL CONSTRAINT [DF_org_defaults_bar_number] DEFAULT(N''),
        [phone] nvarchar(100) NOT NULL CONSTRAINT [DF_org_defaults_phone] DEFAULT(N''),
        [email] nvarchar(320) NOT NULL CONSTRAINT [DF_org_defaults_email] DEFAULT(N''),
        [address_line_1] nvarchar(500) NOT NULL CONSTRAINT [DF_org_defaults_address_1] DEFAULT(N''),
        [address_line_2] nvarchar(500) NOT NULL CONSTRAINT [DF_org_defaults_address_2] DEFAULT(N''),
        [division_head_name] nvarchar(400) NOT NULL CONSTRAINT [DF_org_defaults_division_head] DEFAULT(N''),
        [row_section_head_name] nvarchar(400) NOT NULL CONSTRAINT [DF_org_defaults_section_head] DEFAULT(N''),
        [chief_legal_counsel_name] nvarchar(400) NOT NULL CONSTRAINT [DF_org_defaults_chief_counsel] DEFAULT(N''),
        [updated_at] nvarchar(50) NOT NULL,
        [updated_by_user_id] uniqueidentifier NULL,
        [updated_by_display] nvarchar(400) NULL,
        [row_version] rowversion NOT NULL,
        CONSTRAINT [CK_organization_defaults_singleton] CHECK([id]=1),
        CONSTRAINT [FK_organization_defaults_updater] FOREIGN KEY([updated_by_user_id]) REFERENCES [$(Schema)].[app_users]([id])
    );
END;

IF NOT EXISTS(SELECT 1 FROM [$(Schema)].[organization_defaults] WHERE [id]=1)
BEGIN
    DECLARE @json nvarchar(max)=(SELECT TOP(1) [value] FROM [$(Schema)].[app_settings] WHERE [key]=N'org_defaults_json');
    INSERT INTO [$(Schema)].[organization_defaults]
        ([id],[attorney_name],[bar_number],[phone],[email],[address_line_1],[address_line_2],
         [division_head_name],[row_section_head_name],[chief_legal_counsel_name],[updated_at],[updated_by_display])
    VALUES
        (1,
         COALESCE(JSON_VALUE(@json,'$.AttorneyName'),N''),
         COALESCE(JSON_VALUE(@json,'$.BarNumber'),N''),
         COALESCE(JSON_VALUE(@json,'$.Phone'),N''),
         COALESCE(JSON_VALUE(@json,'$.Email'),N''),
         COALESCE(JSON_VALUE(@json,'$.AddressLine1'),N''),
         COALESCE(JSON_VALUE(@json,'$.AddressLine2'),N''),
         COALESCE(JSON_VALUE(@json,'$.DivisionHeadName'),N''),
         COALESCE(JSON_VALUE(@json,'$.RowSectionHeadName'),N''),
         COALESCE(JSON_VALUE(@json,'$.ChiefLegalCounselName'),N''),
         CONVERT(nvarchar(50),SYSUTCDATETIME(),127),
         CASE WHEN @json IS NULL THEN N'System default' ELSE N'SQLite migration' END);
END;
