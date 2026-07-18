SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF COL_LENGTH(N'$(Schema).discovery_template_items',N'created_by_user_id') IS NULL ALTER TABLE [$(Schema)].[discovery_template_items] ADD [created_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).discovery_template_items',N'row_version') IS NULL ALTER TABLE [$(Schema)].[discovery_template_items] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).discovery_base_versions',N'created_by_user_id') IS NULL ALTER TABLE [$(Schema)].[discovery_base_versions] ADD [created_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).discovery_base_versions',N'row_version') IS NULL ALTER TABLE [$(Schema)].[discovery_base_versions] ADD [row_version] rowversion NOT NULL;

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_discovery_template_items_creator') EXEC(N'ALTER TABLE [$(Schema)].[discovery_template_items] ADD CONSTRAINT [FK_discovery_template_items_creator] FOREIGN KEY([created_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_discovery_base_versions_creator') EXEC(N'ALTER TABLE [$(Schema)].[discovery_base_versions] ADD CONSTRAINT [FK_discovery_base_versions_creator] FOREIGN KEY([created_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).discovery_template_items') AND name=N'IX_discovery_template_items_id_version') CREATE INDEX [IX_discovery_template_items_id_version] ON [$(Schema)].[discovery_template_items]([id],[version]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).discovery_base_versions') AND name=N'IX_discovery_base_versions_id_version') CREATE INDEX [IX_discovery_base_versions_id_version] ON [$(Schema)].[discovery_base_versions]([id],[version]);
