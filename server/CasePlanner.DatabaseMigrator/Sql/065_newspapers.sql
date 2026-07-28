SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Newspaper of general circulation reference lookup (final implementation, item 7). Unlike
-- circuit_clerks/assessors/collectors (053-055), a county can have MULTIPLE newspapers, so this is
-- NOT one-row-per-county: [county] has no unique constraint, there is no seed data (rows are added
-- by staff as needed, never pre-populated), and the row is addressed by its own [id] for every
-- update - true per-row CRUD, the first reference table in this app shaped that way. Cross-linked
-- from a case's Service & Publication tab by county, same as Circuit Clerk. [is_active] is a
-- soft-disable flag (Attorneys/Legal Assistants convention) - no hard delete endpoint.

IF OBJECT_ID(N'$(Schema).newspapers','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[newspapers]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_newspapers] PRIMARY KEY,
        [county] nvarchar(100) NOT NULL,
        [name] nvarchar(200) NOT NULL,
        [is_general_circulation] bit NOT NULL CONSTRAINT [DF_newspapers_is_general_circulation] DEFAULT(0),
        [publication_days_frequency] nvarchar(200) NULL,
        [submission_deadline] nvarchar(200) NULL,
        [contact_name] nvarchar(200) NULL,
        [phone] nvarchar(100) NULL,
        [email] nvarchar(200) NULL,
        [address] nvarchar(1000) NULL,
        [billing_affidavit_contact] nvarchar(200) NULL,
        [typical_cost] decimal(10,2) NULL,
        [notes] nvarchar(1000) NULL,
        [is_active] bit NOT NULL CONSTRAINT [DF_newspapers_is_active] DEFAULT(1)
    );
    CREATE INDEX [IX_newspapers_county] ON [$(Schema)].[newspapers]([county]);
END;
