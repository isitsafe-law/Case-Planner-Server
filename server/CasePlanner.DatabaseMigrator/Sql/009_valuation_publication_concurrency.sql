SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF COL_LENGTH(N'$(Schema).valuation_positions',N'row_version') IS NULL ALTER TABLE [$(Schema)].[valuation_positions] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).valuation_positions',N'is_deleted') IS NULL ALTER TABLE [$(Schema)].[valuation_positions] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_valuation_positions_is_deleted] DEFAULT(0);
IF COL_LENGTH(N'$(Schema).valuation_positions',N'deleted_utc') IS NULL ALTER TABLE [$(Schema)].[valuation_positions] ADD [deleted_utc] datetime2 NULL;
IF COL_LENGTH(N'$(Schema).valuation_positions',N'deleted_by_user_id') IS NULL ALTER TABLE [$(Schema)].[valuation_positions] ADD [deleted_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).comparable_sales',N'row_version') IS NULL ALTER TABLE [$(Schema)].[comparable_sales] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).comparable_sales',N'is_deleted') IS NULL ALTER TABLE [$(Schema)].[comparable_sales] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_comparable_sales_is_deleted] DEFAULT(0);
IF COL_LENGTH(N'$(Schema).comparable_sales',N'deleted_utc') IS NULL ALTER TABLE [$(Schema)].[comparable_sales] ADD [deleted_utc] datetime2 NULL;
IF COL_LENGTH(N'$(Schema).comparable_sales',N'deleted_by_user_id') IS NULL ALTER TABLE [$(Schema)].[comparable_sales] ADD [deleted_by_user_id] uniqueidentifier NULL;
IF COL_LENGTH(N'$(Schema).publication_dates',N'row_version') IS NULL ALTER TABLE [$(Schema)].[publication_dates] ADD [row_version] rowversion NOT NULL;
IF COL_LENGTH(N'$(Schema).publication_dates',N'is_deleted') IS NULL ALTER TABLE [$(Schema)].[publication_dates] ADD [is_deleted] bit NOT NULL CONSTRAINT [DF_publication_dates_is_deleted] DEFAULT(0);
IF COL_LENGTH(N'$(Schema).publication_dates',N'deleted_utc') IS NULL ALTER TABLE [$(Schema)].[publication_dates] ADD [deleted_utc] datetime2 NULL;
IF COL_LENGTH(N'$(Schema).publication_dates',N'deleted_by_user_id') IS NULL ALTER TABLE [$(Schema)].[publication_dates] ADD [deleted_by_user_id] uniqueidentifier NULL;

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_valuation_positions_deleted_by_user') EXEC(N'ALTER TABLE [$(Schema)].[valuation_positions] ADD CONSTRAINT [FK_valuation_positions_deleted_by_user] FOREIGN KEY([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_comparable_sales_deleted_by_user') EXEC(N'ALTER TABLE [$(Schema)].[comparable_sales] ADD CONSTRAINT [FK_comparable_sales_deleted_by_user] FOREIGN KEY([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_publication_dates_deleted_by_user') EXEC(N'ALTER TABLE [$(Schema)].[publication_dates] ADD CONSTRAINT [FK_publication_dates_deleted_by_user] FOREIGN KEY([deleted_by_user_id]) REFERENCES [$(Schema)].[app_users]([id]);');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).valuation_positions') AND name=N'IX_valuation_positions_case_deleted') CREATE INDEX [IX_valuation_positions_case_deleted] ON [$(Schema)].[valuation_positions]([case_id],[is_deleted]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).comparable_sales') AND name=N'IX_comparable_sales_case_deleted') CREATE INDEX [IX_comparable_sales_case_deleted] ON [$(Schema)].[comparable_sales]([case_id],[is_deleted]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).publication_dates') AND name=N'IX_publication_dates_case_deleted') CREATE INDEX [IX_publication_dates_case_deleted] ON [$(Schema)].[publication_dates]([case_id],[is_deleted]);
