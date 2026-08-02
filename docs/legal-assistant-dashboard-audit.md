# Legal Assistant Dashboard and Event Preparation — Current-State Audit and Design

Status: design/audit completed 2026-08-02  
Scope: role routing, assistant coverage, pre-filing operations, event preparation, service/publication support, reminders, permissions, and manager summaries.

## Executive conclusion

The application does not currently have a role-routed Legal Assistant dashboard. The current Dashboard route renders the attorney dashboard, while Division Overview is the manager experience. However, the application already has most of the operational records needed to build an assistant experience without creating a parallel task or checklist system:

- staff-directory attorneys and legal assistants, including many-to-many attorney support ties;
- explicit, multiple `case_legal_assistants` rows;
- primary/supporting attorney assignment rows;
- pipeline current holder, stage, sent-to-holder dates, pre-filing milestone history, and pipeline handoffs;
- ordinary checklist tasks and deadlines with one assignee field, provenance, and generated/manual-date history;
- hearing/event records, including authoritative `Jury Trial` events and multi-day dates;
- defendant rows, defendant-linked service-log entries, publication summary, and publication-date/proof records;
- in-app notifications, attorney Action Queue projections, case notes, documents, and activity/audit history.

The safe implementation is therefore additive and phased. Phase 1 should establish identity/routing and assistant scope, then render a compact dashboard from existing records. Event preparation should follow by adding a nullable relationship from existing tasks/deadlines to an event. A new preparation-item type, second checklist system, or dashboard-only status field is not justified.

## Current-state findings

### Roles, routing, and identity

| Area | Current implementation | Reusable rule/test | Gap or risk |
|---|---|---|---|
| Authenticated identity | `AuthenticatedUserProfile` has Entra id, name, email, roles, `IsManager`, and manager tier. | `/api/auth/me`; Entra/security tests. | No Legal Assistant role is represented in the profile or actor-role mapping. |
| Dashboard routing | `App.tsx` uses manually selected `page` state. `Dashboard` renders the attorney dashboard; `managerDashboard` renders the manager dashboard. | Existing dashboard and manager component tests. | No role-based automatic assistant route. |
| App roles | Entra configuration currently distinguishes user/admin and manager state. | `CaseAccessService` and Entra middleware. | Legal Assistant must be added as an application/profile role or directory-linked role; SQLite has no authenticated actor. |
| Multiple roles | Entra profile already carries a list of roles and manager flags. | Auth profile serialization. | No explicit precedence rule for a user who is both manager and Legal Assistant. Recommended precedence: Administrator/Manager keeps management dashboard; Legal Assistant is used only when no higher management role applies. |
| Staff directory | `AttorneyRecord` and `LegalAssistantRecord` are separate reference records. Assistants contain `AttorneyIds`/`AttorneyNames` and optional `LinkedUserId`. | `StaffDirectoryTests`; dual-provider migration 038. | Staff-directory names are not themselves authenticated permissions. |
| Assistant-to-attorney ties | Many-to-many `legal_assistant_attorneys` exists in the SQL pilot schema and the SQLite staff-directory projection exposes the tie. | Staff-directory read/write paths. | A current-user-to-staff-directory resolution must be made authoritative through `LinkedUserId` when Entra is enabled. |
| Case assistant assignment | `case_legal_assistants` is an explicit one-to-many snapshot list and supports multiple assistants. | `CaseLegalAssistantTests`; `/api/cases/{id}/legal-assistants`. | It is not automatically synchronized with every supported attorney; keep explicit case rows as overrides/coverage records. |
| Case attorney assignment | `case_attorney_assignments` supports Primary/Supporting rows; `cases.assigned_attorney` remains a compatibility projection. | Attorney-assignment tests and audit events. | Assistant scope must union all primary/supporting attorneys, not only the legacy primary string. |
| Access scope | `CaseAccessService` filters authenticated SQL/Entra users by case assignments; SQLite/local mode is unrestricted. | Assignment-filtered dashboard tests. | Portable mode cannot prove real role visibility without a preview actor or Entra. Do not imply that local unrestricted access is production permission behavior. |

### Pre-filing and filing

| Area | Current implementation | Reusable rule/test | Gap or risk |
|---|---|---|---|
| Case state | `CaseStatus` includes Pipeline and Filed / Service Pending; legacy status/stage/track fields remain for compatibility. | Status model and pipeline tests. | Assistant dashboard should use consolidated case status, not recreate a second status. |
| Current holder | `CaseRecord.CurrentHolder`, `PipelineStage`, `DateSentToCurrentHolder`, and `NextReviewDate`; compact pre-filing card edits holder. | `PreFilingMilestoneTests`, pipeline handoff tests. | Good source for “On My Desk,” “With Attorney,” and aging. Holder values need a final normalized vocabulary. |
| Milestones | Pre-filing milestone store records Pleadings Package Sent and Chief Counsel Signatures Received with completion date, actor, note, mark/unmark, and audit history. | `PreFilingMilestoneTests`. | Package-level document list/review metadata is not a separate durable package object. |
| Handoffs | `pipeline_handoffs` records previous/new holder/stage, date, review date, and note. Dedicated pipeline-handoff endpoint exists. | Pipeline handoff tests and activity history. | This is sufficient for history; add package-level projections only if the UI cannot derive the required view. |
| Review decisions | Pre-filing review events and pipeline holder approvals exist. Returned-for-revision actions can move work back toward the assistant. | Holder approval/review-note tests. | Need an assistant-specific projection, not another approval log. |
| Filing transition | Case status and filing fields are updated through existing case workflow; Filed / Service Pending is the operational post-filing state. | Case status/promotion tests. | Exact UI should confirm filing date, case number, court, and division in one transition. |
| Documents | Initial and routine documents are generated from approved templates and remain drafts for user review. | Document generation tests and merge-field matrix. | No durable link from a generated document to a pipeline package, event, or review request was found. |

### Events and preparation

| Area | Current implementation | Reusable rule/test | Gap or risk |
|---|---|---|---|
| Event model | `HearingRecord`/`Hearing` stores case, title, exact event type, start/end date, time, location, description, timestamps, and row version. | Hearing/event tests. | Naming is legacy (`hearings` table) but the current event vocabulary is shared. |
| Jury Trial | `Jury Trial` is the authoritative event type used by case header, calendar, reports, and KPI views. | Upcoming-trial/report tests. | Event preparation now reuses ordinary linked work; richer event-specific preparation templates remain incremental. |
| Event permissions | Event POST/DELETE uses ordinary case write access. Date-change proposals now have a separate audited request path and approval decision. | Endpoint access metadata/tests. | Broader event-create/delete role restrictions and Entra-specific policy remain follow-up work. |
| Tasks/deadlines | `ChecklistItemRecord` and `DeadlineItem` are existing work records with one assignee/name, due date, status, notes, generated provenance, and history/manual flags. They now accept nullable `RelatedEventId`. | Work Queue/date-edit/generation tests. | Event preparation still relies on ordinary work records rather than a separate item type. |
| Templates | Existing checklist/deadline templates generate ordinary work and retain source template/version/stage data. Event-specific candidates can use the proceeding date and suppress duplicates per event. | Workflow generation tests. | The initial candidate/apply flow is intentionally small; broader template administration remains incremental. |
| Date recalculation | Generated event-linked work stores event relationship and relative offset; preview/apply shifts open, non-overridden dates and records activity/history. | Workflow-generation and deadline history tests. | The current UI uses a compact review flow; richer batch previews can follow. |
| Preparation page | Case Events and Calendar now link to a focused event-preparation workspace that composes existing work records. | Event UI tests. | The current page is optimized for the first operational slice and can gain richer documents/notes panels later. |

### Work Queue, Action Queue, reminders, and documents

| Area | Current implementation | Gap or risk |
|---|---|---|
| Work Queue | Unified view of tasks, deadlines, discovery, service, and other operational items. | Event context and assistant-supported-attorney scope are missing. |
| Action Queue | Attorney-oriented case action projection from `AttorneyDashboardEngine`/`AttorneyDashboardComposer`; supports decisions, deferment, notes, holder, discovery, and deadline actions. | It is not a generic assistant reminder-request system. Repeated assistant reminders must update one request/history rather than create duplicate attorney queue records. |
| Notifications | Durable in-app notifications exist for selected assignment/completion/reminder triggers; email is separately configured and may be disabled. | No dedicated “assistant requested attorney action” record with repeated reminder history/resolution was found. Do not claim email delivery. |
| Follow-up fields | Case-level next action/review/follow-up fields and dashboard thresholds exist. | These are broad case fields and should not be duplicated for event preparation. |
| Templates | Settings exposes template management; generation produces a draft for review. | Assistant use of approved templates is conceptually compatible; template editing must remain restricted. |
| Document workflow | Generation audit/activity exists. | No document-level review/comment/workflow relation to an event or package was found. Defer unless required by the first phase. |

### Service and publication

| Area | Current implementation | Reusable rule/test | Gap or risk |
|---|---|---|---|
| Defendants | `CaseDefendantRecord` is a one-to-many canonical case child with name, role, address, service method/date, answer fields, and notes. | Defendant tests. | It is suitable as the party anchor for service logs; no separate party registry is required for this dashboard phase. |
| Service attempts | `ServiceLogEntry` supports optional `CaseDefendantId`, party-name snapshot, status, method, event date, notes, and timestamps. | Service-log and service-status tests. | Current entry delete endpoint is available to ordinary case writers; assistant “no delete” is a permission refinement still needed for authenticated roles. |
| Service status | `ServiceStatusEngine` and service queue calculate case-level review/urgency from filing/service/publication data. | Service status tests. | Assistant dashboard should reuse these rules and show defendant detail only after opening a case/section. |
| Publication | Canonical publication summary plus `publication_dates` records first/second publication, newspaper, proof filed/date, service resolved, and notes. | Publication tests. | Covered-defendants association is not currently represented; add only if required by real publication workflows. |
| Proofs | Proof-filed fields exist for publication entries; service logs retain notes/status. | Publication/service tests. | Proof-of-service document/task linkage is not first-class. |
| Thresholds | Existing service status rules cover approximately 60, 90, 105/115, and 120-day review/urgency. | `ServiceStatusEngineTests`. | Reuse shared thresholds; do not create dashboard-only day counts. |

### Permissions and manager views

| Area | Current implementation | Gap or risk |
|---|---|---|
| Case writes | `CaseAccessService.CanWriteAsync` and assignment-aware endpoint metadata protect authenticated provider paths; SQLite is intentionally unrestricted. | Legal Assistant role permissions are not yet encoded. |
| Protected data | Settlement/valuation fields are present in case workspace and manager/attorney workflows. | Need explicit assistant deny rules once role identity exists; frontend hiding alone is insufficient. |
| Case close/reopen | Broadly available in SQLite preview from prior testability decisions; expected to be gated with Entra. | Assistant must not receive close/reopen rights in production role policy. |
| Delete operations | Case children generally expose delete endpoints subject to case write access. | Assistant-specific restrictions for service attempts, filing records, uploaded files, and templates need server-side policy. |
| Manager dashboard | `ManagerDashboard.tsx` provides division/attorney workload, pipeline, events, attention, data-quality, and staffing views. | No assistant workload/risk summary; add exception/risk context, not raw productivity ranking. |
| Manager data | Existing reports include legal-assistant load/caseload context in places. | Consolidate only after assistant ownership/scope is authoritative. |

## Proposed role and scope behavior

1. Add a normalized Legal Assistant role to the authenticated profile/actor model for Entra deployments. Keep the existing `LinkedUserId` on the staff-directory row as the bridge to the operational assistant record.
2. Resolve supported attorneys from the linked `LegalAssistantRecord` relationship. One assistant may support many attorneys; one attorney may have many assistants.
3. Assistant case scope is the union of cases assigned to supported primary/supporting attorneys plus explicit `case_legal_assistants` assignments. A named assistant work item owner remains the ownership source for “On My Desk.”
4. Use explicit case assistant rows as coverage/manual assignments, not an automatically rewritten copy of attorney support ties.
5. Provide an attorney filter defaulting to “All supported attorneys.” Do not expose a routine dashboard switch to the attorney dashboard.
6. Managers continue using Division Overview/manager dashboard. They receive assistant workload and risk summaries there rather than opening an assistant dashboard as another role.
7. Recommended precedence: Administrator/Manager dashboard first; Legal Assistant dashboard for a non-manager Legal Assistant; attorney dashboard otherwise. A future user with multiple operational roles needs an explicit preference only if the office actually creates such users.
8. SQLite preview currently has no authenticated user. A real role-routed assistant dashboard cannot be permission-tested there without either Entra or an explicit development-only preview actor. If preview testing is required, add a clearly labeled local role selector/test identity rather than treating unrestricted SQLite access as security validation.

## Proposed dashboard information architecture

The assistant dashboard should be a work-operations view, not a copy of the attorney action dashboard.

1. **On My Desk** — assistant-owned open tasks/deadlines, returned corrections, filing work, service follow-up, and litigation support.
2. **Waiting on Attorney** — current assistant-owned or assistant-requested work blocked on a specific attorney review/action, with age and next follow-up.
3. **Upcoming Proceedings and Preparation** — events within a selectable 30/60/90/120/180/all-upcoming horizon, default 180 days; compact rows with open/overdue/waiting counts and next due date.
4. **Pre-Filing and Document Preparation** — package rows grouped by holder/stage, with time in current step and handoff action.
5. **Overdue Assistant Work** — only owned/actionable work, not every event without preparation.
6. **Service and Publication** — case-level exception summaries sourced from the existing service/publication rules; defendant detail on open.
7. **Filed Cases Needing Support** — compact exceptions, not a duplicate docket.

Initial KPI cards should be: On My Desk, Waiting on Attorney, Upcoming Proceedings, Preparation Needs Attention, and Service Follow-Up. Empty exception sections should collapse to a small empty-state row rather than leave a large blank card.

## Event-preparation design

Use a dedicated event-preparation page. A full page is preferable to a modal because it must eventually contain linked tasks/deadlines, documents, notes, reminders, handoffs, template provenance, recalculation preview, and audit history. Case Events and dashboard rows should show only a compact derived summary and an Open Preparation link.

Minimum additive relationships:

- nullable `RelatedEventId` on checklist items and deadlines;
- nullable template-item provenance fields if not already available for a generated item;
- relative offset/anchor metadata for event-generated items;
- explicit calculated/manual-override indicator where current provenance cannot distinguish the two;
- provider-parity migrations for SQLite and SQL Server before using the relationship in shared queries.

Preparation summary is derived from linked underlying work:

- no linked items: No tracked preparation;
- open items with none completed: Not started;
- some completed and some open: In progress;
- open attorney-assigned items: Waiting on Attorney;
- any open overdue item: Needs Attention;
- all linked items complete: Preparation Complete.

The existing 120-day Jury Trial template should be offered first. Hearing and Deposition templates should be small optional templates, not mandatory large checklists. Template application must be idempotent by event/template-item identity and allow review/removal/assignment/date adjustment before generation.

When an event date changes, show a preview, recalculate only incomplete generated items still using their original offset, preserve manual overrides and completed work, avoid duplicates, and record the result in audit history. Until these fields and rules exist, do not claim that current case-level generation safely recalculates event preparation.

## Attorney reminder design

The current Action Queue is attorney-oriented and should be reused only as the destination/projection, not copied into a second reminder system. Add a durable reminder/request record or extend the existing action model only after confirming the current SQL/SQLite provider contract. It must have one stable request identity, requested action, attorney, requested-by assistant, requested completion date, next follow-up date, comments, status, and append-only history. Repeated reminders append history. Resolving the request clears the queue projection but does not complete the underlying task. Until email integration is enabled, the UI must say “follow-up recorded” or “request created,” never “email sent.”

## Pre-filing package design

Start with the current holder, milestone, handoff, review-note, activity, and document records. A package-level projection can group the foundational documents without introducing a second document review system. The assistant dashboard should show:

- On My Desk;
- Returned for Corrections;
- Ready to Send to Attorney;
- With Attorney;
- With Deputy Chief Counsel;
- With Chief Counsel;
- Approved/Ready to File.

The package row should derive current status from the existing holder/stage and timestamps. Add a durable package entity only if existing records cannot provide package-level comments, reviewer, or document inclusion without ambiguity. Filing should continue through the existing Filed / Service Pending transition and confirm filing date, case number, court, and division.

## Service/publication design

Reuse `CaseDefendantRecord`, `ServiceLogEntry`, `PublicationRecord`, `PublicationEntryRecord`, and `ServiceStatusEngine`. Assistants may add/edit service records and prepare proof-related work; authenticated role policy should prevent deletion. Attorney confirmation of perfected service remains a separate protected action. Show 60–70 day review, 90-day extension review, and 120-day escalation from the shared service rules. Keep publication case-level unless real use proves that covered-defendant association is necessary.

## Event-change approval

Court-event date proposals now preserve the confirmed event date until an authorized decision is recorded. Approved proposals trigger linked preparation-date recalculation while preserving completed and manually overridden work. The remaining gap is broader role enforcement for event creation/deletion once Entra-backed permissions are enabled.

## Manager additions

Add compact assistant context to Division Overview only after ownership and role scope are authoritative:

- open/overdue assistant-owned work;
- waiting-on-attorney requests;
- pre-filing packages by holder/stage;
- service cases at shared thresholds;
- event-preparation risks with an actual overdue/open condition;
- temporary coverage/unassigned work.

Do not rank assistants by raw completion counts, documents generated, or clicks. Present risks and coverage needs.

## Recommended phases

### Phase 0 — this audit (complete)

Document the current state, source-of-truth decisions, SQLite/Entra limitations, and provider gaps.

### Phase 1 — role, scope, and dashboard shell

Add Legal Assistant to the authenticated role/profile model, resolve the linked staff-directory assistant, add supported-attorney scope and filter, establish server-side scope helpers, and render a distinct dashboard using existing events, cases, pre-filing projections, work items, service status, and assistant rows. Add a local preview actor only if testability is required.

### Phase 2 — pre-filing operations

Build the assistant package projection from existing milestones/holders/handoffs/review notes/documents; add assistant-specific handoff actions and permissions; preserve all existing history.

### Phase 3 — event preparation

Add event relationships to existing tasks/deadlines, focused preparation page, compact event summaries, trial/hearing/deposition template application, idempotency, date recalculation preview, manual override preservation, and audit history.

### Phase 4 — reminders

Add stable assistant reminder requests backed by the existing Action Queue projection, repeated history, follow-up dates, resolution, and no false email claim.

### Phase 5 — service/publication

Apply assistant permissions to service/publication operations, improve defendant-level exception presentation, and connect proof-related work without adding a duplicate checklist.

### Phase 6 — manager summaries

Add assistant risk/coverage summaries to Division Overview using the same server-side projections and metric definitions.

## Migration and test implications

No destructive migration is justified by this audit. Required additive work is likely:

- Legal Assistant role/profile and authenticated staff-link support;
- provider-parity event relationship columns on checklist/deadline records;
- event-generated offset/anchor metadata where current provenance is insufficient;
- a reminder request/history record or carefully scoped extension of an existing action record;
- only later, a package entity or publication covered-party relation if current records prove inadequate.

Tests should be added in phase order: role routing and supported-attorney scope; multiple assistants/attorneys and coverage; dashboard KPI/empty/filter behavior; event-linked work and derived summaries; template idempotency/date recalculation/manual overrides; reminder deduplication/resolution; assistant permission boundaries; service thresholds; and manager exception summaries. Use fixed dates and test both SQLite preview behavior and authenticated SQL/Entra behavior where available.

## Explicit non-goals

- no renamed copy of the attorney dashboard;
- no new preparation work-item type;
- no second checklist/task system;
- no manually maintained preparation-status field;
- no automatic failure flag merely because an event has no generated preparation;
- no assistant access to settlement/valuation evaluation fields;
- no assistant close/reopen, service-attempt deletion, uploaded-file deletion, or approved-template editing;
- no claim that email was sent before email integration is active.
