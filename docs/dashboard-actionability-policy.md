# Dashboard Actionability Policy

## Purpose

This document explains why a case appears on an attorney-facing dashboard or work queue. It is the
single reference for actionability rules while the application remains a portable SQLite preview.

The system should never present an unexplained warning. Every actionable row should identify the
signal that caused it, the relevant date or threshold, and the next suggested action.

## Scope and exclusions

The active dashboard scope includes open cases: Pipeline, Filed / Service Pending, Active Litigation,
Settlement Pending, and Trial Preparation. Triage and Resolved / Closed cases are excluded from normal
action queues and alerts unless a view explicitly opts in. A future deferral date suppresses ordinary
action-queue signals until the return date.

Events are not themselves Work Queue items. A hearing, deposition, mediation, or trial appears in the
Calendar and can create a court-event signal when it is approaching. Tasks, deadlines, discovery, and
service work are the ordinary Work Queue sources.

## Attorney Action Queue

The Action Queue consolidates multiple signals into one row per case. The most urgent matched signal
drives the displayed category, reason, timing, and next action; the row retains the related-warning
count so additional reasons are not lost.

| Signal | Default trigger | Default category | Can be preference-adjusted? |
|---|---|---|---|
| Court event soon | Jury trial or other scheduled proceeding within 30 days | Court events soon | Lookahead may be adjusted; the event itself remains visible in Calendar |
| Hard deadline overdue | Open deadline due today or earlier | Needs action now | No; overdue remains visible |
| Hard deadline soon | Open deadline due within 14 days | Hard deadlines soon | Lookahead may be adjusted |
| Service risk | Service deadline is missing, upcoming, urgent, or overdue | Service risk | Presentation bands may be adjusted, but statutory/operational dates remain visible |
| Checklist task due | Open checklist item due today or earlier | Needs action now | No for overdue tasks; future task lookahead may be adjusted later |
| Discovery blocked | Discovery item is waiting/follow-up and its follow-up or due date has passed | Blocked | The attorney may filter or defer review, but the underlying overdue item remains in Work Queue |
| Stale review | No meaningful activity beyond the threshold for the case stage | Stale review | Yes; stage thresholds are policy defaults |

The queue is sorted by named priority tier, then earliest review date, then longest inactivity. It does
not use an unexplained numerical health score.

## Pipeline actionability

Pipeline tracts use stricter eligibility so normal review handoffs do not overwhelm the queue. A
pre-filing tract appears in the attorney's main action queue when at least one of these is true:

- The current holder is the attorney.
- The case is marked Priority or Rushed.
- The waiting follow-up date has arrived.

The pipeline monitoring view can additionally flag a missing holder or stage, a returned revision that
has remained unresolved, or at least 60 days without meaningful pipeline movement. A normal-priority
tract held by deputy chief counsel does not automatically appear in the attorney's main queue.

## Momentum and waiting rules

- No waiting owner/date: 60 days without meaningful activity becomes Stalled.
- Waiting owner without a follow-up date: Review Required immediately because the waiting record is incomplete.
- Future follow-up date: Waiting Appropriately; the case is not treated as stale solely because it is waiting.
- Passed follow-up date: Review Required and returned to the Action Queue under Escalate.

The meaningful-activity clock is intentionally narrower than every database edit. Activity types that
represent substantive legal progress advance the clock; routine notes and unrelated edits do not.

## Trial watch and service monitoring

Trial Watch includes a case manually marked Trial Track or with a trial date within 180 days. Service
monitoring uses filing-date bands: day 60 is an attorney check-in, day 90 begins management-visible
risk, days 105 and 115 increase urgency, and day 120 is due/overdue. Routine day-60 check-ins do not
automatically appear in the manager Needs Attention list.

## Fixed rules versus configurable policy

The future Settings implementation should distinguish these categories:

### Fixed

- Overdue court, service, and hard-deadline conditions
- Required filing or service data that prevents a safe next step
- Case access and lifecycle exclusions

### Configurable defaults

- Court-event lookahead days
- Hard-deadline lookahead days
- Stage-specific stale-review thresholds
- Pipeline stall threshold
- Whether informational findings appear in the primary queue
- Default queue filters and preferred horizon

### Attorney preferences

After Entra identity is available, each attorney may adjust presentation preferences such as earlier
lookahead, informational-item visibility, default filters, and workday/calendar horizon. Preferences
must not hide legally overdue or operationally blocked work. SQLite preview mode may use one shared
default profile until identity exists.

## Required explanation on every actionable item

Each row should expose a concise explanation, for example:

```text
Why this is here: Jury trial is in 12 days.
Threshold: Court-event lookahead is 30 days.
Next action: Prepare trial/hearing checklist.
```

For stale review:

```text
Why this is here: No meaningful activity in 47 days.
Threshold: Discovery-stage review threshold is 30 days.
Next action: Review case status.
```

If several signals match, show the primary reason and indicate that additional warnings are available.

## Current implementation boundary

The current rules are implemented in `AttorneyDashboardEngine`, `DashboardTriageEngine`, pipeline
stall detection, service-risk calculation, and the Work Queue selectors. The SQLite preview now stores
the five configurable defaults in `app_settings` and exposes them through Settings → Dashboard
Actionability. The live attorney dashboard consumes the stored values for momentum, pipeline stall,
discovery cutoff, trial preparation, and Trial Watch behavior. Invalid values are rejected and the
legal/operational overdue signals remain fixed.

Attorney-specific overrides are intentionally deferred until Entra identity is available. Until then,
the stored policy is a shared local default for the portable build.

The shared upcoming-work projection now carries `WhyThisIsHere` and `PolicyThreshold` for tasks,
deadlines, discovery follow-ups, and service work. The dashboard's Overdue Work and Due in the Next
7 Days rows display those values, so a user can see whether the row is present because it is overdue,
due today, approaching, missing a due date, or governed by a fixed operational date. SQLite fallback
data uses the same wording rules when the server projection is temporarily unavailable.

No automatic repair should be performed when a case becomes actionable. The dashboard recommends a
next step; the attorney remains responsible for reviewing and changing the case record.
