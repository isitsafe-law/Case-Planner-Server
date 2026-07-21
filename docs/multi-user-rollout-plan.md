# Multi-User Rollout & Reporting — Findings and Phased Plan

Started: 2026-07-20 · Completed: 2026-07-21 · Status: **all phases built, verified, committed, and pushed to `main`**

## Status summary (2026-07-21)

Everything scoped below shipped. The sections after this one are the original investigation/plan and are kept for historical context — a few things were decided or discovered differently once actual building started; those are called out inline where it matters, and summarized here:

| Phase | What shipped | Notable deviations from the original plan |
|---|---|---|
| **1** — Roster/assignment/visibility | `case_role`/`assignment_role` on `case_assignments`, admin-gated case deletion (client+server), "My Cases/All Cases" toggle | Matches plan; confirmed SQL-Server-only functional, dormant until Entra is live |
| **2** — Opposing attorneys, task assignment | `case_opposing_attorneys` child table (full dual-provider); `checklist_items.assigned_user_id` (SQL-Server-only functional) | Matches plan |
| **3a/3b** — Witness registry | `witness_persons` + fuzzy name matching (`WitnessNameMatcher`) + cross-case lookup with live-pulled trial/deposition dates | No backfill migration, per explicit instruction — registry starts empty and grows from new adds only |
| **4a/4b/4c** — Notifications | In-app bell + email (SMTP, disabled by default), triggers for task-assigned/task-completed/deadline-reminder, per-user in-app/email preferences, admins as system-wide recipients | Required a new durable `app_users.is_administrator` flag — "admin" was previously a pure per-request claims check with no row a `BackgroundService` could query |
| **5 (data capture)** | `FinalJudgmentAmount`, `DispositionType`, `TakingType`, `District` fields; a proper Close Case dialog | **District auto-fills from County** via a verified real ARDOT county→district mapping (all 75 counties), not the plan's "new field, not derivable" — still independently overridable |
| **5 (Staff Directory)** | New dual-provider `attorneys`/`legal_assistants`/tie tables, seeded with the office's real roster; `AssignedAttorney` converted from free text to this directory | **Not in the original plan at all.** Built because "by attorney" reporting needed a stable identity that works without waiting on the dormant Entra roster — deliberately separate from `app_users` |
| **5 (Manager tier)** | `app_users.is_manager`, admin-grantable, lets Managers edit the Staff Directory alongside Admins | Also not in the original plan — added when the office asked for a middle permission tier distinct from Administrator |
| **5 (tracking fix)** | Every path that changes `CurrentHolder`/`PipelineStage` now logs to `pipeline_handoffs`, not just the Handoff dialog | Discovered as a data-completeness gap while scoping Report C; historical gaps before the fix are unrecoverable |
| **5 (Reports A/B/C)** | Caseload & Workload, Just-Compensation/Outcome (with required per-attorney context), Cycle-Time — all as new Reports-tab sub-views | Reports A/B are 100% client-side per the original plan; **Report C needed one new endpoint** (`GET /api/work-queues/pipeline-handoffs`, nullable-`caseId` bulk fetch) since cross-case handoff history didn't exist client-side anywhere |

The original investigation below predates all of this and is left as-is for history; treat the table above as the authoritative current state.

## Decisions confirmed (2026-07-20)

1. **Auth = Entra ID**, "Windows login" = Entra/hybrid-joined SSO. Confirmed.
2. **SQL Server is the multi-user target.** SQLite stays a single-user/testing sandbox indefinitely — new multi-user tables/logic do not need SQLite parity going forward (a departure from this session's usual strict dual-provider discipline, deliberately, per this decision).
3. **Witness matching: fuzzy, not exact-only.** Flag on likely-similar names ("Max" vs. "Maxwell"), user can always override and keep typing. Cheap string-similarity approach (no new dependency) planned; can be tuned later if it over/under-flags in practice.
4. **Notifications: in-app AND email.** Both channels are the plan (email needs an SMTP relay or Graph API access from IT — a real external dependency, not just a build task).
5. **"Region" = District, not County.** Multiple counties per district — this is a new field to capture, not derivable from existing data.

## Decisions confirmed (2026-07-20, round 2)

6. **No supervising-attorney hierarchy.** Correction to the "still needed" item below: `app_users` does not need a role/reporting-line concept. Assignment is purely per-case — managers add/remove/swap people directly on a case's assignment list at any time. A second attorney is just a second Attorney-tagged assignment on the same case, not a separate field or a roster relationship. (Superseded the schema addition originally described two paragraphs down — see Phase 1 for the actual, smaller addition: a `case_role` label on `case_assignments`, not on `app_users`.)
7. **Case visibility is never restricted by role.** Non-admins can see all cases, unconditionally — "My Cases / All Cases" is a view preference, not an access gate. This is exactly how Phase 1 was scoped (an additive client-side filter, deliberately not touching the existing hard `GetVisibleCaseIdsAsync` restriction mechanism, which stays dormant).
8. **Admins-only case deletion.** New, specific: delete-a-case must be gated to admins only, client AND server-side (client-side gating alone is bypassable). Not yet built — queued as the next fix after Phase 1 lands.
9. **Case-opening/creation authority may be restricted — undecided.** Explicitly not a decision yet ("we'll see"). No action; flagged so it isn't lost.
10. **A future general admin/permissions dashboard is anticipated.** Not building this now, but the Attorneys & Staff Settings screen (Phase 1) should be designed so it can grow into a broader permissions panel later rather than needing a rebuild — e.g. don't hard-code it as single-purpose-roster-only in a way that would make adding more permission controls awkward.

## Correction: the backend is further along than the first pass found

A deeper trace turned up more than the first investigation caught. **The visibility-filtering mechanism is not just built, it's already wired into nearly every read endpoint in `Program.cs`** — dashboard, upcoming work, case list, workspace, service queue, deadlines, checklist, discovery, and more all already call `access.GetVisibleCaseIdsAsync(token)` and filter their results by it. Today this is a no-op (Entra disabled → always returns `null` → unrestricted) but the enforcement path is live code, not a gap.

Further, a **full admin API for assignment management already exists**: `GET/POST /api/admin/case-assignments`, `DELETE /api/admin/case-assignments/{caseId}/{userId}`, `GET /api/admin/users`, `PUT /api/admin/users/{userId}/active` — plus `GET /api/auth/me` for current-user identity. All of it calls the same `SqlServerCaseAssignmentRepository` described above.

**What this means for Phase 1's real scope** (narrower than originally estimated):
- ~~Build assignment enforcement~~ — already done, needs Entra switched on (an IT ops task) to take effect.
- ~~Build an assignment API~~ — already done.
- **Still needed:** an attorney/legal-assistant *role and reporting-line* concept (`app_users` currently just tracks "an authenticated Entra identity," not "this person is an Attorney supervising these Legal Assistants") — one small schema addition, not a new subsystem.
- **Still needed:** all client UI — a roster management screen, case-level assignment UI (attorney/second-attorney/auto-derived LA), and a "My Cases / All Cases" toggle on the case list. 100% greenfield on the client regardless of how much backend already exists.
- Second-attorney-on-a-case falls out of the existing model for free — the assignment table already supports multiple users per case with independent roles; a second attorney is just a second `Owner`/`Collaborator` row, no schema change needed.

**Testing caveat:** none of the `SqlServer*`-prefixed code can be exercised against a real SQL Server in this sandbox (no pilot instance available here) — same limitation the project's own IT docs already acknowledge (SQL-Server-specific behavior is validated against the real approved pilot instance, not in local dev). I can verify the client UI and request/response shapes fully; end-to-end backend behavior against live SQL Server + live Entra needs to happen in that real environment.

Scope: items 6–12 from the "usage notes / change requests" doc (shared witness list, attorney/LA assignment, case visibility, multiple opposing attorneys, task assignment, notifications, reporting). Items 1–5 (near-term fixes) are being built separately.

---

## 1. What already exists

This is the headline finding: **a real multi-user foundation is already built**, just dormant. It was put in during an earlier SQL Server pilot-readiness pass and has never been switched on or given a UI.

| Piece | Where | State |
|---|---|---|
| Entra ID (Azure AD) OIDC auth | `Program.cs`, `appsettings.json: Authentication:Entra` | Fully wired, `Enabled: false` |
| User provisioning on login | `Security/EntraUserProvisioningMiddleware.cs` | Provisions an `app_users` row from Entra claims on first authenticated request |
| Per-case assignment w/ roles | `Security/SqlServerCaseAssignmentRepository.cs` | Real table (`dbo.case_assignments`), roles are **Owner / Collaborator / ReadOnly**, full CRUD, audit-logged |
| Visible-case-ID scoping | `Security/CaseAccessService.cs: GetVisibleCaseIdsAsync` | Already computes exactly "which case IDs can this user see" — `null` = unrestricted/admin, otherwise a real `HashSet<long>` |
| Read/write permission check | `CaseAccessService.CanReadAsync/CanWriteAsync` | Already role-aware |
| Admin role | `EntraOptions.AdministratorAppRole` | Exists as a concept |
| Audit trail | `dbo.audit_events` | Real table, already used by the assignment repository |

**The catch:** every piece above is `SqlServer*`-prefixed and reads from `dbo.*` tables that only exist in the SQL Server pilot schema. The SQLite path (what actually runs today) has **no equivalent** — no local `app_users`/`case_assignments` tables, no middleware wiring, no UI anywhere that reads `GetVisibleCaseIdsAsync` or manages an assignment. It's real, tested-shaped infrastructure sitting unused, not a stub.

**On "tied to Windows login":** ARDOT's own prior IT documentation (`docs/it-first-machine-checklist.md`) already frames the eventual auth path as Entra, not raw on-prem Windows Integrated Auth (Kerberos/NTLM). If ARDOT's Windows machines are Entra-joined or hybrid-joined (typical for a Windows-login SSO experience today), Entra ID *is* "tied to Windows login" from the user's chair — you're never shown a separate login screen. I'm treating this as the same mechanism unless told otherwise (see Q1).

## 2. What's genuinely greenfield

Checked and confirmed absent — no scaffolding, no partial version:

- **Attorney/legal-assistant roster.** No roster table, no manager-assignment UI. Cases have a single free-text `assignedAttorney` string field — no structured attorney identity, no LA-to-attorney tie, no second-attorney slot.
- **Shared witness list.** `Witness` is 100% per-case (name/side/role/subpoena status), no shared identity, no cross-case linkage, no fuzzy-name matching anywhere in the codebase.
- **Notifications.** No email/SMTP, no in-app notification center, no push mechanism of any kind.
- **Case-close outcome fields.** `DepositAmount` exists and is real. `Final Judgment/Settlement Amount`, `Disposition Type`, and `Taking Type` do not exist anywhere as case fields (a `SettlementAmount` field does exist, but it's a Settlement Justification *document generation* runtime input, not persisted case data — unrelated to the reporting spec's need).
- **Reporting engine.** Today's "Reports" tab is a case-list-with-filters CSV/Excel export. Nothing computes deltas, aggregates by disposition/attorney/region, or does trend-over-time.

## 3. Proposed phase order

Dependency-driven, not priority-driven — an earlier phase is a prerequisite for what follows it.

### Phase 0 — Architecture decision (blocks everything)
The dormant foundation is SQL-Server-only. Multi-user visibility/assignment cannot work on the SQLite path as-is. Before any of Phases 1–4 can start, this needs a call: **build multi-user support directly on the SQL Server pilot schema (completing the cutover this rollout implies), or port the same assignment/visibility model down to SQLite first** so the office can pilot multi-user features before/without a full SQL Server migration. This is the single biggest fork in the whole plan — see Q2.

### Phase 1 — Identity backbone: roster + assignment + visibility (items 7, 8)
- Build the attorney/legal-assistant roster (manager-editable; "system could populate it" — likely seeded from existing `assignedAttorney` string values as a one-time migration, deduplicated, then managers clean up from there).
- Extend case assignment to a structured `Attorney` (primary) + `SecondAttorney` (nullable, for trial pairs) + auto-assigned `LegalAssistant` (derived from the attorney's roster tie).
- Wire the existing (or SQLite-ported) `GetVisibleCaseIdsAsync` into the case-list/dashboard queries, default filtered to "my cases," with an explicit toggle to "everyone's cases."

### Phase 2 — Straightforward extensions (items 9, 10)
- Multiple opposing attorneys: `opposingCounsel` (currently one string) becomes a list.
- Task assignment: `checklist_items` gains an assignee (a legal assistant from the Phase 1 roster), surfaced in the Work tab and Work Queue the same way `currentHolder` already is.

Both are simple once Phase 1's roster exists; low risk to build in parallel with Phase 1's later half.

### Phase 3 — Shared witness registry (item 6)
Independent of auth — can run in parallel with Phase 1 if useful. Needs a new global `witnesses` (person) table separate from the per-case witness link, a similarity-matching approach for the autofill/flag behavior (see Q3), and a unique ID per person that per-case witness rows reference instead of duplicating name/contact data.

### Phase 4 — Notifications (item 11)
Needs Phase 1 (who to notify) and Phase 2 (task assignment, so "task complete" has a clear next-recipient) done first, plus a delivery-channel decision (see Q4) since none exists today.

### Phase 5 — Reporting (item 12)
Two independent tracks:
- **Data capture can start immediately, unblocked by everything else**: add `FinalJudgmentAmount`, `DispositionType` (Jury Trial/Settlement/Mediation), required at case close; add `TakingType` (Partial/Full/TCE) at case setup. The sooner this starts, the more historical closes have complete data by the time reporting ships — I'd recommend building this now rather than waiting on Phase 0–4, independent of the rest of this document.
- **The report engine itself** depends on Phase 1 for "by attorney" breakdowns (needs real attorney identity, not a free-text string) and benefits from Phase 4's audit trail for time-in-holder/time-in-phase cycle metrics. Region/district reporting needs a location field this app doesn't currently capture at the right granularity (see Q5).

---

## 4. Questions

1. **Auth mechanism** — confirming Entra ID (with Entra/hybrid-joined Windows machines giving the "just works" SSO feel) is what "tied to Windows login" means, versus literal on-prem Windows Integrated Authentication (Kerberos against local AD, no cloud identity at all) — a materially different, older mechanism ASP.NET Core also supports but which this app has zero scaffolding for today.
2. **SQLite vs. SQL Server for multi-user** — should Phase 1 target completing the SQL Server cutover (using the existing dormant infrastructure as-is), or port the assignment/visibility model to SQLite so multi-user features can pilot before a full database migration?
3. **Witness similarity matching** — how aggressive should "flag when there's an existing exact or similar name" be? A cheap, dependency-free approach (normalized-string / edit-distance matching, e.g. Levenshtein or a phonetic key) can catch "Maxwell" vs "Max" reasonably well without any new infrastructure; a fuzzier vendor/ML approach would catch more but costs more to build and tune. I'd lean toward the cheap approach first and revisit if it under- or over-flags in practice.
4. **Notification delivery** — in-app only (a notification bell/center inside the app, seen next time someone opens it), email (needs IT to provide an SMTP relay or Graph API access), or both? This is a real infrastructure dependency, not just a UI choice.
5. **Region/district for reporting** — is this `County` (already captured on every case) or a different geographic grouping (a legislative/ARDOT district that doesn't map 1:1 to county)? If it's just County, item B's region reporting is nearly free; if it's a different grouping, that's a new field to capture.

*(Historical note: at the time this was written, nothing above had been built yet. See the Status summary at the top of this document for what actually shipped and how a few of these answers evolved once building started — most notably District ended up auto-derived from County after all (Q5), and reporting's "by attorney" grouping ended up powered by a new dual-provider Staff Directory rather than waiting on the dormant Entra roster.)*
