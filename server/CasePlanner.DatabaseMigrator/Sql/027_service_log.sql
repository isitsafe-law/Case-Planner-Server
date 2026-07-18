SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Per-party service-of-process tracking (Group D of the search/dashboard/case-record cleanup
-- pass). A case can have multiple defendants being served separately; this is a new multi-row
-- table distinct from the single-value servicePerfected/serviceMethod/servicePerfectedDate
-- fields on the cases table, which only ever held one value for the whole case.
--
-- This creates the schema only. The runtime IServiceLogStore SQL Server implementation is a
-- deliberate NotSupportedException stub for now, matching the unified document platform's
-- precedent (see 023_document_platform.sql) - there is no SQL Server sandbox available yet to
-- build and verify a real implementation against.

IF OBJECT_ID(N'$(Schema).service_log_entries','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[service_log_entries]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_service_log_entries] PRIMARY KEY,
        [case_id] bigint NOT NULL,
        [party_name] nvarchar(400) NOT NULL,
        [status] nvarchar(50) NOT NULL CONSTRAINT [DF_service_log_entries_status] DEFAULT(N'Not Served'),
        [method] nvarchar(200) NULL,
        [event_date] date NULL,
        [notes] nvarchar(max) NULL,
        [created_at] datetime2 NOT NULL CONSTRAINT [DF_service_log_entries_created] DEFAULT(SYSUTCDATETIME()),
        [updated_at] datetime2 NULL,
        [row_version] rowversion NOT NULL
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).service_log_entries') AND name=N'IX_service_log_entries_case')
    CREATE INDEX [IX_service_log_entries_case] ON [$(Schema)].[service_log_entries]([case_id]);
