SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

UPDATE [$(Schema)].[checklist_templates]
SET [stage]=CASE [stage]
    WHEN N'Intake & Filing' THEN N'Pipeline'
    WHEN N'Service' THEN N'Filed / Service Pending'
    WHEN N'Discovery & Evaluation' THEN N'Active Litigation'
    WHEN N'Trial Track' THEN N'Trial Preparation'
    WHEN N'Resolved' THEN N'Resolved / Closed'
    ELSE [stage] END
WHERE [stage] IN (N'Intake & Filing',N'Service',N'Discovery & Evaluation',N'Trial Track',N'Resolved');

UPDATE [$(Schema)].[checklist_template_items]
SET [phase]=CASE [phase]
    WHEN N'Intake & Filing' THEN N'Pipeline'
    WHEN N'Service' THEN N'Filed / Service Pending'
    WHEN N'Discovery & Evaluation' THEN N'Active Litigation'
    WHEN N'Trial Track' THEN N'Trial Preparation'
    WHEN N'Resolved' THEN N'Resolved / Closed'
    ELSE [phase] END
WHERE [phase] IN (N'Intake & Filing',N'Service',N'Discovery & Evaluation',N'Trial Track',N'Resolved');
