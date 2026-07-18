# Architecture plan for workflow simplification

## Current state

- `cases.status` is lifecycle/triage/closed; `cases.stage` drives litigation automation; `cases.track` selects stage paths and template eligibility; `pipeline_stage` is a separate pre-filing handoff field.
- Tasks live in `checklist_items` and deadlines in `deadlines`. Checklist templates are database-backed; deadline templates are database-backed seeded rules. Both now carry structured provenance plus legacy `source_type` for compatibility.
- Discovery documents use file-backed authoritative templates plus database-backed versioned issue/tag items and immutable generation snapshots.
- Service facts are stored on `cases`; publication facts are canonical in `case_publications` with legacy `publication_dates` retained for historical compatibility.
- Generated documents are stored as immutable `document_exports` records and files under the local exports folder.

## Consolidation decision

Introduce a user-facing `case_status` projection with the values Pipeline, Filed / Service Pending, Active Litigation, Settlement Pending, Trial Preparation, and Resolved / Closed. Preserve current `status`, `stage`, `track`, and historical values during migration; use a mapping/report rather than destructive replacement. Automation continues to read the existing fields until the mapping is reviewed, so imports and deadline behavior remain safe.

Supporting facts remain separate: service perfected, publication, discovery complete, trial date, waiting, deferment, holder, settlement authority, default actions, and closure result.

## Migration and risk controls

- New manually created cases default to Pipeline while imported cases remain Triage.
- Ambiguous stage/track mappings are reported and left reviewable; no historical dates or generated records are deleted.
- Existing generated documents remain snapshots and are never regenerated in place.
- The attached Interrogatories.txt is the authoritative combined base; RequestsForAdmission.txt is the authoritative RFA base. Internal merge-field mappings translate their PascalCase tokens to case fields.

## Components to reuse/replace

- Reuse `CasePlannerRepository` transactions/backups, `DocumentGenerationEngine`, `IssueTagDiscoveryContent`, `TriageWizard`, `ActionQueueItemCard`, and existing template/version APIs.
- Replace the dense task/deadline review table with the existing picker shell plus compact collapsed rows and an expanded comparison detail.
- Replace individual discovery-item settings with bulk document editors backed by versioned text snapshots.
- Move service/publication presentation into Case Record and retain Status as a compact summary only if needed for queue warnings.
