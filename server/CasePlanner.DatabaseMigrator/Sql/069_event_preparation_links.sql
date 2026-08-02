SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Event preparation uses ordinary checklist/deadline records. These nullable links add context
-- without introducing a second work-item type. Existing rows remain case-level work.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'$(Schema).deadlines') AND name=N'related_event_id')
    ALTER TABLE [$(Schema)].[deadlines] ADD [related_event_id] bigint NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'$(Schema).checklist_items') AND name=N'related_event_id')
    ALTER TABLE [$(Schema)].[checklist_items] ADD [related_event_id] bigint NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_deadlines_related_event')
    ALTER TABLE [$(Schema)].[deadlines] ADD CONSTRAINT [FK_deadlines_related_event]
        FOREIGN KEY ([related_event_id]) REFERENCES [$(Schema)].[hearings]([id]);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_checklist_items_related_event')
    ALTER TABLE [$(Schema)].[checklist_items] ADD CONSTRAINT [FK_checklist_items_related_event]
        FOREIGN KEY ([related_event_id]) REFERENCES [$(Schema)].[hearings]([id]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).deadlines') AND name=N'IX_deadlines_related_event')
    CREATE INDEX [IX_deadlines_related_event] ON [$(Schema)].[deadlines]([related_event_id]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'$(Schema).checklist_items') AND name=N'IX_checklist_items_related_event')
    CREATE INDEX [IX_checklist_items_related_event] ON [$(Schema)].[checklist_items]([related_event_id]);
