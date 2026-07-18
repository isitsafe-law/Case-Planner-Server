# Attorney Dashboard — Technical Note

Companion to the "Attorney Dashboard Redesign" implementation (`server/CasePlanner.Web.Server/Services/AttorneyDashboardEngine.cs`, `CasePlannerRepository.GetAttorneyDashboardAsync`, `client/src/dashboard/`). Explains the scoring/sorting logic, the rules behind each section, and where the implementation made a judgment call instead of following an exact, literal rule from the brief.

## Dashboard scoring and sorting

There is no numerical health score anywhere in this feature — every ranking is either a named priority tier or a plain date/count comparison, matching the brief's explicit "no unexplained scores" rule.

- **Action Queue priority** is one of 4 named tiers (`AttorneyDashboardEngine`'s `Priority1Immediate`..`Priority4Planned`), assigned by *which signal fired*, not a computed score. A case can trip several signals at once (e.g. a missed deadline **and** an unselected discovery strategy); the engine keeps every matched signal, picks the lowest-numbered (most urgent) one to drive the displayed category/reason/next-action, and reports `RelatedWarningCount` = total signals matched. This is the "consolidate multiple warnings into one entry" rule.
- **Sort order**: priority level, then review date (earliest first, undated last), then days-since-meaningful-activity (most inactive first) — implemented exactly as `.OrderBy(priorityLevel).ThenBy(reviewDate).ThenByDescending(daysInactive)` in `GetAttorneyDashboardAsync`.
- **Summary cards** (`SummaryCounts`) are computed over the **entire active docket**, ignoring whatever `ActionQueueFilters` the attorney currently has applied. This is deliberate: if the cards shifted as filters were applied, they'd stop being a stable reference point. The cards and the query-parameter filters are two independent filtering mechanisms — see "Two kinds of filtering" below.

## Meaningful activity rules

`activity_log` is a new table, separate from the pre-existing `MAX(updated_at)`-based "last activity" proxy that `CaseAttentionEngine` (the Cases list) still uses untouched.

- `CasePlannerRepository.RecordActivityAsync` is the **only** place `cases.last_meaningful_activity_date` is written. It checks the activity type against a fixed 19-value allow-list (`MeaningfulActivityTypes`) taken directly from the brief; anything else (including the generic `"Other"` type used by the "Add note" quick action) is logged for the case's history but never advances the clock.
- Nothing else in the app calls `RecordActivityAsync` implicitly. A handful of quick actions do call it explicitly (Record Decision), but most existing mutation paths (editing a case field, checklist/deadline changes, etc.) do **not** — see Assumption 3 below for why this is intentionally narrower than "wire it into every relevant existing mutation."
- 60-day inactivity: `AttorneyDashboardEngine.MomentumStaleDays = 60`. `DaysSinceMeaningfulActivity` falls back to the old `LastActivityAt` proxy only when `LastMeaningfulActivityDate` is null (i.e., before any activity has ever been recorded for that case) — a one-time backfill migration (`MigrateBackfillMeaningfulActivityV1Async`) seeds `LastMeaningfulActivityDate` for every pre-existing case from that same proxy, so the new momentum logic doesn't treat 58 real, currently-healthy cases as having *zero* history the moment this shipped.

## Waiting logic

A case is "waiting" purely by having `WaitingOn` set (no separate boolean flag). `EvaluateMomentumStatus`:

1. No `WaitingOn` → momentum is `Stalled` if `DaysSinceMeaningfulActivity >= 60`, else `Moving`.
2. `WaitingOn` set, `WaitingFollowUpDate` **missing** → `Review Required` immediately. This is the brief's "waiting record is incomplete" trigger — an incomplete waiting record is treated as no better than not waiting at all.
3. `WaitingOn` set, `WaitingFollowUpDate` in the future → `Waiting Appropriately`, regardless of how many days of inactivity have accumulated. A case waiting 500 days on a future follow-up date is never marked overdue.
4. `WaitingOn` set, `WaitingFollowUpDate` has passed → `Review Required`, and it re-enters the Action Queue under **Escalate** (using `WaitingEscalationAction` as the recommended next step if one was recorded).

## Filing pipeline logic

Pre-filing tracts are `cases` rows with `MatterType = "PreFilingTract"` — there's no separate `Matter` table (see Assumption 1). `PipelineStage`/`CurrentHolder` are independent columns, not hard-coded together, per the brief.

- **My Desk** = `CurrentHolder == "Attorney"`. Nothing else about stage matters for this bucket.
- **Action-queue eligibility** (`PreFilingBelongsInActionQueue`) is stricter than "on My Desk": a tract only surfaces in the main queue when it's on the attorney's desk, marked `Priority`/`Rushed`, or its `WaitingFollowUpDate` has arrived. A normal-priority tract quietly sitting with the deputy chief counsel does not clutter the queue.
- **Waiting-tab monitoring flag** (`WaitingMonitorReason`) fires for exactly the brief's five listed exceptions: follow-up date arrived, `Priority`/`Rushed`, missing holder or stage, or ≥60 days with no pipeline movement (reusing `DaysSinceMeaningfulActivity` as a proxy for "pipeline movement," since there's no separate pipeline-specific activity timestamp).
- **Handoffs** (`SavePipelineHandoffAsync`) are transactional: one `pipeline_handoffs` history row plus one `cases` update (holder, stage, date sent, next review) in the same write, so history and current state can never drift apart.

## Discovery warning logic

`DiscoveryPosture` is a new one-row-per-case table, deliberately separate from the pre-existing `discovery_tracking` table (which stays exactly as it is — a per-request/per-item log of individual discovery requests, not a case-level strategy). A case can match **multiple** of the 9 named conditions simultaneously (e.g. "cutoff approaching" and "deficiencies unresolved" at once) — `EvaluateDiscoveryConditions` returns a list, not a single enum, per the brief's explicit "do not reduce discovery to a single yes/no field" instruction.

"Strategy not selected" is checked first and short-circuits everything else (a case with no strategy can't simultaneously be "cutoff approaching"). "No discovery currently needed" similarly short-circuits.

## Trial-watch rules

`IsTrialWatchEligible` is true when `TrialTrack` is manually set **or** the trial date falls within `DefaultTrialWatchDays = 180`. There's no field capturing "settlement appears unlikely" as a distinct signal — the brief's own third eligibility bullet ("attorney marks the case Trial Track") *is* the mechanism for that judgment call; a genuinely separate "settlement unlikely" flag would have needed a new field with no other consumer, so it wasn't added (see Assumption 4).

The 20%-fee-comparison note (`BuildFeeComparisonNote`) is computed **only** for cases that already passed trial-watch eligibility, and only ever produces the fixed neutral sentence from the brief ("Trial consideration: ... Review before final trial valuation...") — never a bare number or a red/yellow/green flag. It is not surfaced anywhere else in the app.

## Assumptions

1. **No `Matter`/`Project`/`Activity`/`User` tables** — the app had none of these before this feature, only `cases`. A pre-filing tract is a `cases` row with `MatterType = "PreFilingTract"` (case number was already optional from earlier work this session). Project Watch groups `cases` by `(ProjectName, JobNumber)` at query time rather than through a dedicated `projects` table.
2. **No auth system exists anywhere in the app** (single-user local desktop tool). "Authorization checks consistent with the rest of the system" is satisfied by there being none; `PipelineHandoffRecord` has no `CreatedByUserId`.
3. **`RecordActivityAsync` is not wired into every existing mutation path.** It's called explicitly by the new "Record decision" quick action and by the test fixtures/integration tests; it is *not* automatically triggered by, say, saving a discovery-tracking item or editing a case field. Doing so for all ~19 activity types across the existing 5,300-line repository was out of scope for this pass — the manual "Record decision" action is the documented catch-all, matching the brief's own inclusion of that action in the quick-actions list.
4. **Shared-project-issue detection is intentionally narrow.** It fires only when 2+ tracts in the same project share a non-empty `Appraiser` value and at least 2 of them are independently `Stalled` — a real, verifiable signal already in the data model. It does **not** attempt to detect "repeated valuation theories," "common access claims," or "common drainage claims," since nothing in the current schema captures legal theory or claim type. Extending this would require new structured fields, not just new logic.
5. **Component naming**: two component names in the brief collide with type names already needed elsewhere (`ActionQueueItem`, `ProjectWatchRow` are both natural type names for the API response shapes). The components are named `ActionQueueItemCard` and `ProjectWatchRowCard` to avoid the collision; they otherwise match the brief's spec 1:1.
6. **Testing infrastructure was bootstrapped from zero** — no `.sln`, no xUnit project, and no client test runner existed before this feature. `CasePlanner.Web.Server.Tests` (xUnit, real temp-directory SQLite per test, no mocking) and Vitest + React Testing Library (scoped to `client/src/dashboard/`) were added because the brief requires tests the stack couldn't previously run — not retrofitted onto the rest of the 5,700-line `App.tsx`.
7. **The old Dashboard tab's `DashboardTriageEngine`/`TriageQueue` were not deleted from the backend.** The frontend no longer reads them (confirmed dead code removed from `App.tsx`), but `GetDashboardAsync()` — which still computes them — is also called from `GetCaseWorkspaceAsync` to populate `CaseWorkspaceResponse.OverviewSummary`. That field turned out to already be unused by the frontend too, but removing `GetDashboardAsync`'s triage computation felt like a separate, lower-value cleanup outside this feature's scope; it's cheap to compute and doesn't affect the new dashboard.
