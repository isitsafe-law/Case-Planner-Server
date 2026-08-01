# Case Style, Trial Events, Assignments, and Service Audit

## Implemented in this pass

- Case Style was moved from the prominent Overview area into the lower Case Record card. The stored `cases.case_style` value remains the source used by document generation and exports. Existing edit, rebuild-from-parties, and copy behavior remains available.
- The Add Jury Trial path already creates a `Jury Trial` event directly; the generic Add Event path continues to exclude that event type.
- Startup now performs a one-time additive reconciliation from legacy `cases.trial_date`/`trial_end_date` values to a Jury Trial event when a case has no Jury Trial event. Existing event/date conflicts and multiple Jury Trial events are preserved and counted for review rather than overwritten.
- Editing a Jury Trial continues to update the case-level trial-date compatibility projection. Deleting one synchronizes that projection to the next remaining Jury Trial, or clears it if none remains.

## Current source-of-truth findings

| Area | Current source | Current risk | Decision in this pass |
|---|---|---|---|
| Case Style | `cases.case_style` | None identified; display placement was the issue | Keep authoritative value and move only the UI |
| Jury Trial | `hearings` plus legacy `cases.trial_date` projection | Existing databases can contain either or conflicting representations | Additive reconciliation; preserve conflicts for review |
| Primary attorney | `cases.assigned_attorney` | Only one attorney is represented in current dashboards and permissions | Keep primary field until assignment model is introduced end-to-end |
| Legal assistants | `case_legal_assistants` rows | Names are stored as assignment snapshots | Keep current many-row model; no destructive rewrite |
| Defendants | `case_defendants` | Service Log currently stores a separate free-text party name | Preserve service history; defer FK consolidation to a staged migration |

## Assignment migration boundary

Adding supporting attorneys is not just an extra input. It affects case reads, filtering, attorney dashboards, calendar scope, permission checks, exports, and management counts. The safe next implementation is an additive `case_attorneys` relation with an explicit primary/supporting role, followed by provider-neutral query updates and compatibility reads. The existing primary field should remain until those consumers are migrated and tested.

The first foundation slice is now in place as `case_attorney_assignments` with `Primary` and `Supporting` roles, SQLite repository methods, portable API endpoints, and an Edit Case control. It deliberately does not replace `cases.assigned_attorney`; the control is labeled record-only for a Primary row so users do not mistake this slice for migrated dashboard ownership. SQL Server also has a provider store with row-version concurrency, soft delete, and audit-event writes, but both providers still require deployment/identity validation before the legacy projection can be retired.

Multiple legal assistants are already supported through explicit case rows, so the next improvement should be assignment UX and validation rather than another storage migration.

## Service Log migration boundary

`service_log_entries.party_name` is historical text and must remain available for prior service records. The first additive slice now adds nullable `case_defendant_id`; new entries can select a canonical defendant while the existing party name remains the stored snapshot. Historical rows are not automatically backfilled, and ambiguous/unmatched rows remain untouched. SQLite and SQL Server runtime stores support the bridge; deployment validation remains deferred.

Data Quality now reports duplicate canonical party names within a case. This is observational and does not automatically merge or delete rows, because a duplicate may represent a distinct legal interest or may be referenced by historical service records.

## Verification

- The Jury Trial legacy sample test confirms SAMPLE-CASE-004 receives exactly one Jury Trial event with the preserved start and end dates.
- The focused server tests and client production build pass after the Case Style, trial reconciliation, and canonical service-party changes.
