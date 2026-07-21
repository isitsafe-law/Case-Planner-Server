# Multi-User Rollout — SQL Server Pilot Testing Checklist

Purpose: every feature below was built and unit-tested against SQLite only — there has been no live SQL
Server sandbox available during development, so all `SqlServer*`-prefixed code is verified by compilation
and code review, not execution. This checklist is what IT should work through against the real SQL Server
pilot instance (with Entra actually enabled) before trusting any of it for real case data.

This is scoped to the multi-user rollout specifically — migrations `030` through `039` and the features
built on top of them (roster/assignment/visibility, opposing attorneys, task assignment, the witness
registry, notifications, the Staff Directory, the Manager tier, case close-out/District fields, and the
three-report reporting engine). It assumes the base SQL Server cutover work in `sql-server-migration.md`
(migrations `001`-`029`) and the Entra app registrations in `microsoft-entra-setup.md` are already in place
or being done alongside this. It does not repeat those documents' own checklists — see them for the base
schema/auth setup steps.

Status legend: check each box as it passes. Anything that fails should be filed as a bug against the
specific section below, not worked around by skipping ahead — several later sections depend on earlier
ones (e.g. reporting depends on the Staff Directory and the holder-tracking fix).

---

## 0. Prerequisites before starting

- [ ] A dedicated SQL Server pilot database exists, and migrations `001` through `039` have all been
      applied in order via `CasePlanner.DatabaseMigrator` with no errors.
- [ ] `Authentication:Entra:Enabled = true`, with a real tenant ID, API client ID, SPA client ID, and the
      `CasePlanner.Admin` app role configured per `microsoft-entra-setup.md`.
- [ ] At least **two** test identities are available: one assigned the `CasePlanner.Admin` app role, one
      plain user with neither Admin nor Manager — you need both to test gating correctly.
- [ ] `Database:ActiveProvider` can be pointed at SQL Server for this pilot testing pass (see
      `sql-server-migration.md` for the startup guard around this).
- [ ] If testing email notifications: a real SMTP relay (host/port/credentials/from-address) is available
      to populate `Notifications:Email` in configuration. If this isn't available yet, skip section 6's
      email-specific items and note them as still pending — everything else in this checklist does not
      depend on email.

## 1. Migration & schema sanity

- [ ] `dbo.app_users` has `is_administrator` and `is_manager` columns (migrations `035`, `039`), both
      defaulting to `0`/false on existing rows.
- [ ] `dbo.case_assignments` has a `case_role` column (migration `030`) alongside the pre-existing
      `assignment_role`, constrained to `Attorney`/`LegalAssistant`/`Other`.
- [ ] `dbo.case_opposing_attorneys`, `dbo.witness_persons`, `dbo.notifications`,
      `dbo.notification_preferences`, `dbo.attorneys`, `dbo.legal_assistants`,
      `dbo.legal_assistant_attorneys` all exist (migrations `031`, `033`, `034`, `036`, `038`).
- [ ] `dbo.cases` has `final_judgment_amount`, `disposition_type`, `taking_type`, and `district` columns
      (migration `037`), all nullable.
- [ ] `dbo.checklist_items` has an `assigned_user_id` column (migration `032`).
- [ ] **Staff Directory seed data is present**: exactly 9 rows in `dbo.attorneys` (Michelle Davenport —
      Chief Counsel, Angela Dodson — Deputy Chief Counsel, Helen Newberry, Stephen Lowman, Cody
      Eenigenburg, Iván Martínez, Katie Meister, Michael Bynum, Bailey Gambill) and 3 rows in
      `dbo.legal_assistants` (Tyler Story, Evelyn Allison, Donna Ramsey) with the correct ties in
      `dbo.legal_assistant_attorneys` — see migration `038`'s seed block for the exact expected ties.
      **Confirm "Iván Martínez" round-trips with its accented characters intact** — this was a specific
      risk flagged during development (no ASCII-folding).
- [ ] Re-running the database migrator against the same database does **not** duplicate the Staff
      Directory seed rows (the seed is guarded by "only insert if `attorneys` is empty").

## 2. Entra authentication & the Administrator/Manager roles

- [ ] Logging in with either test identity successfully provisions a row in `dbo.app_users` (external
      subject, tenant ID, object ID, display name, email, `last_login_utc`).
- [ ] The identity assigned the `CasePlanner.Admin` app role shows `is_administrator = 1` in `app_users`
      after login, and `GET /api/auth/me` returns `isAdmin: true` for that session.
- [ ] The plain test identity shows `is_administrator = 0` and `GET /api/auth/me` returns `isAdmin: false`.
- [ ] As the admin identity, `PUT /api/admin/users/{userId}/manager` with `{"isManager": true}` against
      the plain user succeeds, and that user's next `GET /api/auth/me` reflects `isManager: true`.
- [ ] The **plain, non-admin** identity gets `403 Forbidden` calling `PUT /api/admin/users/{userId}/manager`
      directly (Managers cannot grant Manager status to themselves or anyone else — only Admins can).
- [ ] `is_administrator`/`is_manager` are **not** re-derived or reset on subsequent logins beyond what the
      Entra app-role claim says for `is_administrator` (a user manually granted Manager status keeps it
      across logins — Manager is never touched by the Entra claims path).

## 3. Case visibility & assignment (Phase 1)

- [ ] The plain non-admin identity can see **every** case in the case list — visibility is never
      role-restricted; confirm the "My Cases / All Cases" toggle is a view preference only, not a filter
      that hides data from anyone.
- [ ] As admin, `POST /api/admin/case-assignments` can add a user to a case with a `case_role` of
      `Attorney`, `LegalAssistant`, or `Other`, and an `assignment_role` of `Owner`, `Collaborator`, or
      `ReadOnly`.
- [ ] A second `Attorney`-tagged assignment can be added to the same case (no supervising-attorney
      hierarchy — a second attorney is just a second row).
- [ ] `DELETE /api/admin/case-assignments/{caseId}/{userId}` removes an assignment cleanly (this is how a
      case gets "reassigned" — remove then add, no dedicated "swap" action exists).
- [ ] As the plain non-admin identity, `DELETE /api/cases/{id}` returns `403 Forbidden` — case deletion is
      admin-only, enforced server-side (not just a hidden button client-side).
- [ ] As admin, `DELETE /api/cases/{id}` succeeds.

## 4. Opposing attorneys & task assignment (Phase 2)

- [ ] A case can have more than one opposing attorney added via the case editor (`case_opposing_attorneys`
      is a real one-to-many child table, not the old single free-text field).
- [ ] A checklist item can be assigned to a real roster member (`assigned_user_id`) and that assignment is
      visible in the Work tab / Work Queue the same way `currentHolder` already is.

## 5. Witness registry (Phase 3)

- [ ] Typing a witness name that closely matches an existing person in the registry (e.g. "Max" when
      "Maxwell Carter" already exists) surfaces a similar-name suggestion while typing — this must be
      live/debounced, not just an exact-match check at save time.
- [ ] Selecting a suggested match links the new witness row to the existing `person_id` instead of
      creating a duplicate registry entry.
- [ ] Typing a genuinely new name does not force a false-positive match (spot-check a name sharing a
      surname with an existing person but a clearly different given name, e.g. "Ann Carter" vs. an
      existing "Tom Carter" — this exact scenario was a caught-and-fixed false positive during
      development, worth re-confirming on the real data set).
- [ ] The "Other cases" lookup on a linked witness correctly lists every other **open** case they're a
      witness in, with that case's trial date (and range, if set) and any Deposition-type hearing dates —
      pulled live, not double-entered.
- [ ] A witness linked to a person who is also a witness on a **closed** case does NOT show that closed
      case in the "Other cases" list.

## 6. Notifications (Phase 4)

- [ ] Assigning a checklist task to a user creates a `TaskAssigned` in-app notification for that user
      (visible via the header bell), and does **not** re-fire on a no-op resave of the same assignment.
- [ ] Marking a checklist task's status into a completed state notifies every `case_role='Attorney'`
      assignee on that case, **plus every current Administrator system-wide** (regardless of whether that
      admin is personally assigned to the case) — this is the confirmed "admins get notifications too"
      behavior, deduplicated so an admin who's also the case's attorney isn't notified twice.
- [ ] A case's `TrialDate` or `ServiceDeadline120` falling exactly 7 days out (the default
      `Notifications:ReminderLeadDays`) creates a `DeadlineReminder` notification for every assigned
      staffer on that case (any `case_role`, not just Attorney) **plus every current Administrator**.
      Since this is timer-driven (`DeadlineReminderBackgroundService`, a 6-hour scan interval), either
      wait for a real scan cycle or manipulate a test case's date to land exactly on the lead-time
      boundary and force a check.
- [ ] The deadline-reminder scan is idempotent — running it twice (or restarting the service) does not
      create a second notification for the same case/deadline-type/deadline-date combination.
- [ ] With `Notifications:Enabled = true` and real SMTP relay settings configured, a notification that a
      recipient hasn't opted out of actually sends an email via the relay (check the relay's own delivery
      log, not just that the API call didn't error).
- [ ] In Settings → Notifications, turning off "in-app" for a notification type stops that type's
      notifications from being created at all for that user (not just hidden client-side); turning off
      "email" for a type stops email specifically while the in-app notification still appears.
- [ ] The "Email me" master checkbox in that same panel correctly reflects and controls all three
      per-type email checkboxes together.

## 7. Staff Directory & Manager tier

- [ ] With the plain, non-manager, non-admin identity, the Staff Directory panel in Settings shows
      "Admin or Manager only" in place of edit controls — read-only for that user.
- [ ] After being granted Manager status (section 2), that same user **can** edit the Staff Directory:
      add an attorney, change a legal assistant's supported-attorney list (a full replace, e.g. reassign
      Tyler Story from Stephen Lowman/Cody Eenigenburg to a different pair and confirm the old ties are
      gone, not just added-to), and deactivate/reactivate an attorney or legal assistant.
- [ ] The case editor's "Assigned Attorney" field is a dropdown sourced from this directory, and a case
      whose existing `assignedAttorney` value isn't in the current active-attorney list still shows that
      legacy value as a selectable option (grandfathering — nothing silently disappears when an attorney
      is deactivated or renamed).

## 8. Case close-out & District (Phase 5 data capture)

- [ ] Marking a case Closed opens the Close Case dialog and **requires** a Disposition Type (Jury Trial /
      Settlement / Mediation) and a Final Judgment/Settlement Amount before it can be submitted — cancelling
      the dialog leaves the case's status unchanged.
- [ ] Setting a case's County auto-fills District correctly per the real ARDOT mapping (e.g. Pulaski →
      District 6, Washington → District 4) — spot-check a handful of counties across different districts
      against the actual ARDOT map, not just that some value gets filled in.
- [ ] Manually overriding District, then changing County again, does **not** clobber the manual override.
- [ ] Taking Type (Partial / Full / TCE) is settable at case creation/edit and is not required (existing
      cases and imports without it must remain valid).

## 9. Reporting engine (Reports A/B/C)

- [ ] **Caseload & Workload**: with real multi-attorney case data, the "View as" selector correctly scopes
      open-case-by-status counts, trial/deadline density (30/60/90-day windows), and age buckets to a
      single attorney; "Division-wide" shows the full breakdown across attorneys plus a total row. Legal
      Assistant Load is unaffected by the selector and correctly sums each LA's tied attorneys' open cases.
- [ ] **Outcomes**: the coverage line ("N of M closed cases have complete data") reflects real gaps —
      closed cases that predate `FinalJudgmentAmount` capture should NOT be silently averaged in. The
      per-attorney breakdown **always** shows case count, taking-type mix, and deposit range alongside the
      average delta — confirm this context is never dropped even when an attorney has only one or two
      eligible cases.
- [ ] **Cycle Time**: with a few real holder/stage transitions recorded (see section 10), Time-in-Phase and
      Time-in-Holder show non-trivial average days, sorted worst-first. Time-to-Resolution's
      by-disposition-type breakdown always shows all three fixed types even when one has zero eligible
      cases yet.
- [ ] `GET /api/work-queues/pipeline-handoffs` (the bulk cross-case endpoint added for Report C) returns
      handoffs across every visible case, while the pre-existing `GET /api/cases/{id}/pipeline-handoffs`
      still correctly scopes to just one case — confirm the older single-case Handoff-history dialog still
      behaves exactly as before.

## 10. Holder/phase transition tracking completeness

This is a data-integrity fix, not a new feature — confirm it actually closed the gap it was meant to:

- [ ] Changing a case's Current Holder via the case editor's own dropdown (not the dedicated Handoff
      dialog) creates a `pipeline_handoffs` row.
- [ ] Using the quick "Set Holder" action also creates a `pipeline_handoffs` row.
- [ ] Using the dedicated Handoff dialog still works exactly as before (this path already logged
      correctly and was not changed).
- [ ] A case-edit save that changes **both** holder and stage at once produces a single combined
      `pipeline_handoffs` row (not two), matching how the Handoff dialog represents that same situation.
- [ ] Saving a case with holder/stage **unchanged** creates no new `pipeline_handoffs` row.

---

## Sign-off gate

Every `SqlServer*`-prefixed code path exercised above was written against the exact same contract as its
already-tested SQLite counterpart and reviewed line-by-line, but **none of it has executed against a real
SQL Server instance before this checklist**. Do not treat a clean run through sections 0-10 as equivalent
to the SQLite-side automated test suite (263 server tests / 128 client tests as of this rollout's last
commit) — it is the first real execution of this code, not a regression check. File issues against the
specific numbered section above rather than a general "SQL Server pilot" bucket, since several sections
depend on earlier ones passing first (reporting depends on the Staff Directory and the holder-tracking fix;
notifications depend on the Administrator/Manager identity plumbing in section 2).

Once this checklist passes clean, update `docs/multi-user-rollout-plan.md`'s status table to note the pilot
verification date and outcome.
