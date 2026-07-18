# Final status model

The application uses separate fields for separate legal/workflow concepts.

- Lifecycle (`cases.status`): `Triage`, `Active`, `Closed`. Only `Active` participates in live alerts and automation. Existing display aliases such as `Complete` are treated as closed for backward compatibility.
- Track (`cases.track`): `Contested`, `Settlement`, `Default`, `Friendly`. Track selects the allowed stage path and eligible templates.
- Litigation stage (`cases.stage`): `Intake & Filing`, `Service`, `Discovery & Evaluation`, `Trial Track`, `Resolved`.
- Pre-filing pipeline stage (`cases.pipeline_stage`): internal drafting/review handoff state. It must never replace litigation stage.
- Service: `cases.service_status`, `service_required`, `service_perfected`, and optional actual `service_perfected_date`. The record update timestamp is not evidence of the actual service date.
- Publication: one `case_publications` record containing first/second dates, publication name, perfected state, and update metadata. Legacy `publication_dates` rows are historical only.
- Waiting: presence of `waiting_on`/waiting metadata means progress is pending on another person, office, party, or event.
- Deferred: `cases.deferred_until` plus optional reason, timestamp, and user. Deferral does not imply the case is waiting on someone else.
- Task status: stored on `checklist_items`.
- Deadline status: stored on `deadlines`.
- Discovery status: stored on each `discovery_tracking` item; overall strategy/posture is stored separately in `discovery_postures`.

Do not add a new overloaded status column. New workflow states must be assigned to the appropriate concept above or introduced as a separately named field.
