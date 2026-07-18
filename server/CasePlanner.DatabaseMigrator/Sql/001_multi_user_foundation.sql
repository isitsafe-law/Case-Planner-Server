IF OBJECT_ID(N'$(Schema).app_users', 'U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[app_users]
    (
        [id] uniqueidentifier NOT NULL CONSTRAINT [PK_app_users] PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [external_subject] nvarchar(450) NULL,
        [display_name] nvarchar(250) NOT NULL,
        [email] nvarchar(320) NULL,
        [is_active] bit NOT NULL CONSTRAINT [DF_app_users_is_active] DEFAULT (1),
        [created_utc] datetime2 NOT NULL CONSTRAINT [DF_app_users_created_utc] DEFAULT SYSUTCDATETIME(),
        [updated_utc] datetime2 NOT NULL CONSTRAINT [DF_app_users_updated_utc] DEFAULT SYSUTCDATETIME(),
        [row_version] rowversion NOT NULL
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'$(Schema).app_users') AND name = N'UX_app_users_external_subject')
    CREATE UNIQUE INDEX [UX_app_users_external_subject] ON [$(Schema)].[app_users] ([external_subject]) WHERE [external_subject] IS NOT NULL;

IF OBJECT_ID(N'$(Schema).case_assignments', 'U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[case_assignments]
    (
        [case_id] bigint NOT NULL,
        [user_id] uniqueidentifier NOT NULL,
        [assignment_role] nvarchar(100) NOT NULL CONSTRAINT [DF_case_assignments_role] DEFAULT (N'Owner'),
        [assigned_utc] datetime2 NOT NULL CONSTRAINT [DF_case_assignments_assigned_utc] DEFAULT SYSUTCDATETIME(),
        [assigned_by_user_id] uniqueidentifier NULL,
        [row_version] rowversion NOT NULL,
        CONSTRAINT [PK_case_assignments] PRIMARY KEY ([case_id], [user_id]),
        CONSTRAINT [FK_case_assignments_cases] FOREIGN KEY ([case_id]) REFERENCES [$(Schema)].[cases] ([id]),
        CONSTRAINT [FK_case_assignments_users] FOREIGN KEY ([user_id]) REFERENCES [$(Schema)].[app_users] ([id]),
        CONSTRAINT [FK_case_assignments_assigned_by] FOREIGN KEY ([assigned_by_user_id]) REFERENCES [$(Schema)].[app_users] ([id])
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'$(Schema).case_assignments') AND name = N'IX_case_assignments_user_id')
    CREATE INDEX [IX_case_assignments_user_id] ON [$(Schema)].[case_assignments] ([user_id], [assignment_role]);

IF OBJECT_ID(N'$(Schema).audit_events', 'U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[audit_events]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_audit_events] PRIMARY KEY,
        [case_id] bigint NULL,
        [actor_user_id] uniqueidentifier NULL,
        [action] nvarchar(150) NOT NULL,
        [entity_type] nvarchar(150) NOT NULL,
        [entity_id] nvarchar(450) NULL,
        [occurred_utc] datetime2 NOT NULL CONSTRAINT [DF_audit_events_occurred_utc] DEFAULT SYSUTCDATETIME(),
        [correlation_id] uniqueidentifier NULL,
        [details_json] nvarchar(max) NULL,
        CONSTRAINT [FK_audit_events_cases] FOREIGN KEY ([case_id]) REFERENCES [$(Schema)].[cases] ([id]),
        CONSTRAINT [FK_audit_events_users] FOREIGN KEY ([actor_user_id]) REFERENCES [$(Schema)].[app_users] ([id])
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'$(Schema).audit_events') AND name = N'IX_audit_events_case_time')
    CREATE INDEX [IX_audit_events_case_time] ON [$(Schema)].[audit_events] ([case_id], [occurred_utc] DESC);

IF COL_LENGTH(N'$(Schema).cases', N'row_version') IS NULL
    ALTER TABLE [$(Schema)].[cases] ADD [row_version] rowversion NOT NULL;
