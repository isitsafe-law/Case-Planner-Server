SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Build-plan step 7 (cleanup): drops the tag-linking schema added in migration 021
-- (document_tags, document_template_tag_links, discovery_item_tag_links) - confirmed by the
-- Phase 1 audit to be referenced by zero C# code, added ahead of the feature that would have used
-- it and never wired up - plus custom_document_templates, now retired in favor of the unified
-- document_templates/document_template_versions schema (023_document_platform.sql).
--
-- NOT dropped here: discovery_template_items, discovery_base_versions, discovery_generations.
-- Their C# consumers are retired in this same step, but these three tables were never created by
-- an explicit numbered migration in the first place - the SQL Server schema for them is generated
-- at cutover time by introspecting the live SQLite schema (see docs/sql-server-migration.md).
-- Removing them from CasePlannerRepository.SchemaSql is what stops them from being introspected
-- into a fresh SQL Server cutover; nothing to drop here since no SQL Server instance has been
-- stood up yet to run this migration against.

IF OBJECT_ID(N'$(Schema).document_template_tag_links','U') IS NOT NULL
    DROP TABLE [$(Schema)].[document_template_tag_links];

IF OBJECT_ID(N'$(Schema).discovery_item_tag_links','U') IS NOT NULL
    DROP TABLE [$(Schema)].[discovery_item_tag_links];

IF OBJECT_ID(N'$(Schema).document_tags','U') IS NOT NULL
    DROP TABLE [$(Schema)].[document_tags];

IF OBJECT_ID(N'$(Schema).custom_document_templates','U') IS NOT NULL
    DROP TABLE [$(Schema)].[custom_document_templates];
