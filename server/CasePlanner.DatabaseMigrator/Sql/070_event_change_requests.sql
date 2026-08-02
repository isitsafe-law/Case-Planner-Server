SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF OBJECT_ID(N'$(Schema).event_change_requests', N'U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].event_change_requests (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_event_change_requests PRIMARY KEY,
        case_id bigint NOT NULL,
        hearing_id bigint NOT NULL,
        proposed_start_date date NOT NULL,
        proposed_end_date date NULL,
        note nvarchar(2000) NULL,
        status nvarchar(32) NOT NULL CONSTRAINT DF_event_change_requests_status DEFAULT N'Pending',
        requested_by_user_id uniqueidentifier NULL,
        requested_by_display nvarchar(256) NOT NULL,
        requested_at datetime2(7) NOT NULL,
        decided_by_user_id uniqueidentifier NULL,
        decided_by_display nvarchar(256) NULL,
        decided_at datetime2(7) NULL,
        decision_note nvarchar(2000) NULL,
        CONSTRAINT FK_event_change_requests_case FOREIGN KEY (case_id) REFERENCES [$(Schema)].cases(id),
        CONSTRAINT FK_event_change_requests_hearing FOREIGN KEY (hearing_id) REFERENCES [$(Schema)].hearings(id)
    );
    CREATE INDEX IX_event_change_requests_hearing_status ON [$(Schema)].event_change_requests(hearing_id,status,id DESC);
END;
