IF COL_LENGTH(N'$(Schema).cases', N'case_style_formatting_json') IS NULL
    ALTER TABLE [$(Schema).cases] ADD [case_style_formatting_json] nvarchar(max) NULL;
