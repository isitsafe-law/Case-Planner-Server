SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF COL_LENGTH(N'$(Schema).discovery_postures',N'row_version') IS NULL ALTER TABLE [$(Schema)].[discovery_postures] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).discovery_postures',N'updated_by_user_id') IS NULL ALTER TABLE [$(Schema)].[discovery_postures] ADD [updated_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).discovery_postures',N'updated_by_display') IS NULL ALTER TABLE [$(Schema)].[discovery_postures] ADD [updated_by_display] nvarchar(400) NULL;

IF COL_LENGTH(N'$(Schema).pipeline_handoffs',N'row_version') IS NULL ALTER TABLE [$(Schema)].[pipeline_handoffs] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).pipeline_handoffs',N'created_by_user_id') IS NULL ALTER TABLE [$(Schema)].[pipeline_handoffs] ADD [created_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).pipeline_handoffs',N'created_by_display') IS NULL ALTER TABLE [$(Schema)].[pipeline_handoffs] ADD [created_by_display] nvarchar(400) NULL;

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_discovery_postures_updater') EXEC(N'ALTER TABLE [$(Schema)].[discovery_postures] ADD CONSTRAINT [FK_discovery_postures_updater] FOREIGN KEY([updated_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_pipeline_handoffs_creator') EXEC(N'ALTER TABLE [$(Schema)].[pipeline_handoffs] ADD CONSTRAINT [FK_pipeline_handoffs_creator] FOREIGN KEY([created_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).discovery_postures') AND name=N'UX_discovery_postures_case') EXEC(N'CREATE UNIQUE INDEX [UX_discovery_postures_case] ON [$(Schema)].[discovery_postures]([case_id]);');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).pipeline_handoffs') AND name=N'IX_pipeline_handoffs_case_id') EXEC(N'CREATE INDEX [IX_pipeline_handoffs_case_id] ON [$(Schema)].[pipeline_handoffs]([case_id],[id]);');
