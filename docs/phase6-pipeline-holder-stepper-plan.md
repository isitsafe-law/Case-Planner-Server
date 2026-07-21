# Phase 6: Pipeline Holder Stepper — Plan

Date: 2026-07-21 · Status: **confirmed, ready to build**

## Goal

See at a glance where a case currently sits in its internal handling pipeline — who has the file right
now — with the ability to jump directly to any step, forward or backward, as a case gets handed off,
escalated for review, and handed back with changes.

## Investigation findings

Two different "where is this case" concepts exist in the codebase today:

1. **`CaseStatus`** (the 7-value consolidated litigation-lifecycle bucket: Triage / Pipeline / Filed &
   Service Pending / Active Litigation / Settlement Pending / Trial Preparation / Resolved & Closed) —
   reliable and already used throughout Reports/Dashboard, but describes *what phase of litigation* a case
   is in, not *who currently has the file*.
2. **`CurrentHolder` / `pipeline_stage`** (tracked together in `pipeline_handoffs`) — the "who has the file"
   axis. Investigation found this is only half-reliable: `CurrentHolder` has a real, fixed, consistently-set
   vocabulary (`Legal Assistant`, `Attorney`, `Deputy Chief Counsel`, `Chief Counsel`, `Other`), driven by
   three mutation paths (the Handoff dialog, the quick "Set Holder" action, and the case editor), all of
   which now log to `pipeline_handoffs` since the recent tracking-completeness fix. The free-text
   `pipeline_stage` half, however, is **not actually driven** — the Handoff dialog's `submitHandoff` always
   sends `newStage: ''` — so it has no real vocabulary today and is not part of this feature.

## Confirmed decisions

1. **Data source: `CurrentHolder`**, not `CaseStatus` and not the unreliable free-text stage.
2. **Step order**: `Legal Assistant → Attorney → Deputy Chief Counsel → Chief Counsel`, a linear visual
   sequence matching the natural escalation chain. `Other` is a non-sequential catch-all, shown separately
   rather than forced into the line.
3. **Interactivity**: every step (including `Other`) is directly clickable at any time — no forward-only
   gating, no confirmation dialog (this isn't a destructive action). Clicking a non-adjacent step jumps
   straight there. This is deliberately symmetric: a case escalated to Chief Counsel for review can be
   clicked straight back down to Attorney just as easily as Legal Assistant can jump straight up to Chief
   Counsel — the same interaction handles hand-off, escalation, and return-with-changes.
4. **Placement**:
   - The case workspace header — a per-case stepper showing where *this* case currently stands.
   - The Dashboard's existing "Pipeline" tab (`FilingPipelinePanel`) — a summary/count strip above the
     existing card list, showing the division-wide distribution of open cases across the 4 steps + Other.
5. **No new backend work.** `CurrentHolder` is already settable via the existing
   `POST /api/cases/{id}/holder` endpoint, which already logs to `pipeline_handoffs`. The Dashboard's
   existing `attorneyDashboard.filingPipeline.allPipeline` data already carries `currentHolder` per case, so
   the distribution summary is a client-side aggregation over data that's already loaded — not a new query.

## What's being built

- A new, reusable client-side stepper component: renders the 4-step sequence with filled/current/upcoming
  visual states, `Other` as a separate badge/chip, and every step clickable to jump directly to that holder.
- Wired into the case workspace header, calling the existing holder-set mechanism.
- A summary/count strip added to `FilingPipelinePanel` (Dashboard's Pipeline tab), aggregating the
  already-loaded pipeline rows by `currentHolder` — counts per step, no new server endpoint.

## Explicitly out of scope for this phase

- The free-text `pipeline_stage` field — stays as-is, unreliable and unused; not touched.
- `CaseStatus` (the litigation-lifecycle bucket) — a separate, unrelated concept; stays exactly as it is.
- Any change to the Handoff dialog itself, or to what "next review date"/notes mean there.
