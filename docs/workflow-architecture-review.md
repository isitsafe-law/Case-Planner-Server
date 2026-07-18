# Workflow architecture review

## Current architecture

The application is a local React/TypeScript client backed by an ASP.NET Core minimal API and SQLite. `CasePlannerRepository` owns persistence, schema upgrades, backups, import, activity recording, task/deadline generation, and most workflow mutations. `CaseAttentionEngine`, `DashboardTriageEngine`, and `AttorneyDashboardEngine` provide derived queue and warning state. Document rendering is isolated in `DocumentGenerationEngine` and the document catalog.

The database is upgraded in place. Every write goes through the repository's backup-before-write path; backup retention remains 20 files. Existing records are upgraded with additive columns/tables and null-tolerant reads.

## Sources of truth

| Concept | Source of truth | Derived views |
|---|---|---|
| Lifecycle | `cases.status` (`Triage`, `Active`, `Closed`) | dashboard gating, automation gating, triage queue |
| Litigation track | `cases.track` | stage path and template eligibility |
| Litigation stage | `cases.stage` | case header, stage templates, service-stage inference |
| Pre-filing handoff stage | `cases.pipeline_stage` | attorney dashboard only; it is not litigation stage |
| Holder | `cases.current_holder` | dashboard and case header |
| Waiting | `cases.waiting_on`, `waiting_reason`, `waiting_started_date`, `waiting_follow_up_date` | waiting queues and quick actions |
| Deferral | `cases.deferred_until`, `deferred_reason`, `deferred_at`, `deferred_by` | dashboard suppression/return date |
| Service | service columns on `cases` | Status tab and service queue |
| Publication | one `case_publications` row per case | Status tab and service summary; `publication_dates` is legacy history only |
| Issue tags | `case_issue_tags` joined to `issue_tags` | case header and discovery generation |
| Tasks | `checklist_items` | Tasks tab and work queues |
| Deadlines | `deadlines` plus `deadline_history` | Deadlines tab and alerts |
| Discovery tracking | `discovery_tracking` and `discovery_postures` | Discovery tab and queues |
| Meaningful history | `activity_log` plus `activity_log_history` | Recent Activity |

## Duplication found

- `cases.status`, `cases.stage`, and `cases.pipeline_stage` historically used overlapping labels. They now have distinct responsibilities, but callers must continue using the definitions above.
- Legacy `service_notes` and `publication_service_notes` coexist with structured service/publication fields. They are retained only for historical visibility and are not editable in Edit Case.
- Legacy `publication_dates` rows are migrated into `case_publications` and retained only for historical compatibility. New edits use the canonical record.
- Template provenance is encoded in `source_type`. Checklist generation uses a stable template-name/sort-order key; deadline generation still supports historical numeric IDs and a title fallback after reseeding.

## Workflow boundaries

- Imported new cases enter `Triage`. Triage cases are excluded from alerts, generated tasks/deadlines, service warnings, stale warnings, and the default active dashboard.
- Existing cases remain active/closed and are not forced through triage.
- Activation occurs only after the wizard saves confirmed identity, track, stage, and historical anchors.
- Quick workflow mutations call repository services that update the case and append an activity event in the same write transaction.
- Activity corrections append immutable history before updating the visible entry.
- Calculated deadline changes append deadline history and do not overwrite a manually overridden date.

## Recommended next structural extraction

`CasePlannerRepository` is now large enough that future work should extract `ActivityService`, `CaseWorkflowService`, `TemplateGenerationService`, `ImportTriageService`, and `PublicationService`. Extraction should preserve the existing transaction and backup boundary rather than introduce parallel write paths.
