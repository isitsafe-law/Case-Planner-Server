# Unified Dashboard and Actionable KPI Audit

Status: implementation baseline, 2026-08-01

This audit records the dashboard source-of-truth review for the first controlled KPI package. It is intentionally narrower than a full dashboard rewrite: the SQLite preview does not yet have Entra identity, and the existing provider-neutral dashboard query is the safest place to continue consolidating rules.

## Current inventory

### Attorney dashboard

| Section | Source | Interaction | Assessment |
| --- | --- | --- | --- |
| Headline/context | `/api/dashboard` plus `/api/dashboard/attorney` | Mostly static | Keep as context; not an actionable KPI |
| Priority KPI tiles | `attorneyDashboard.actionQueue` | Filters Action Queue by priority | Keep, but supplement with date-based KPIs |
| Action Queue | `GetAttorneyDashboardAsync` / `AttorneyDashboardEngine` | Case opening, inline decisions, notes, discovery updates | Keep; this is the primary reason-based action list |
| Case Insight | attorney dashboard response | Docket, discovery, momentum, pipeline, trials, projects | Keep as compact planning information |
| Overdue work | shared `/api/dashboard/upcoming-work` projection | Complete, reschedule, open case | Keep; count and rows use the same eligible-work projection as Work Queue |
| Due in next seven days | shared projection | Complete, reschedule, open case | Keep; excludes overdue items |
| Visual summaries | Compact count chips and planning row over shared work/hard-date records | Count click opens Work Queue or Calendar | Keep; large bars were removed |

### Division Overview

| Section | Source | Interaction | Assessment |
| --- | --- | --- | --- |
| Events next 7/30 days | client `hearings` feed | Opens manager calendar | Redefine as hard-date KPIs in the management view |
| Needs-attention count | broad `attentionStatus`/posture warning | Opens Needs Attention tab | Repair: management exceptions must use rule-based rows |
| Pipeline count | all loaded cases | Opens Pipeline tab | Keep only as context; do not present as a problem by itself |
| Open tract count | `isOpenForDivision` | Static | Keep as workload context, not a productivity measure |
| By Attorney | client grouping by primary attorney | Sort, expand, export | Keep as transparent workload context; do not rank |
| Needs Attention | `NeedsAttentionTab` | Export and open case | Keep; service escalation begins at day 90 and fee-shift is not inferred from stage |
| Calendar | manager event feed | Horizon and case navigation | Keep as hard-date destination |
| Data quality | `/api/data-quality` | Filter, export, open samples | Keep below operational KPIs |

## KPI contract for the first package

### Attorney

1. Overdue: incomplete actionable work with a date before the local today, excluding events and completed/deleted/closed records.
2. Due Today: incomplete actionable work due on local today.
3. Next 7 Days: incomplete actionable work due today through seven calendar days, excluding overdue records.
4. Action Queue: explainable reason-coded cases that require a decision, review, preparation, or escalation.
5. Hard Dates Within 90 Days: qualifying hearings, jury trials, depositions, mediations, and court/deadline records; ordinary tasks and follow-up dates are excluded.

The first three are computed from the shared upcoming-work projection. Action Queue remains the provider-neutral attorney dashboard response. Compact planning counts use the same open-case event/deadline catalog already used by the dashboard calendar, with events routed to Calendar and deadlines routed to the Work Queue deadline facet.

### Manager

1. Management Attention: rule-based exception rows only; routine reminders and ordinary inactivity are not promoted as management exceptions.
2. Hard Dates Within 90 Days: qualifying division events/deadlines; a case/event is counted once per record and supporting assignments do not duplicate the division total.
3. Jury Trials Within 180 Days: jury-trial records, with primary/supporting attorney context for staffing review.
4. Pipeline Stalls: existing pre-filing milestone aging detector and configured threshold.
5. Service Risk at 90+ Days: the existing service-risk calculation, beginning at day 90 for management; day 60 remains an attorney check-in only.

## Calculation and scope findings

- Primary attorney is the owner for caseload context. Supporting attorneys are shown in staffing/trial context and must not add a second division case total.
- Work Queue and dashboard work now share `ActionableWorkQueryRules` through `/api/dashboard/upcoming-work`. The client must not invent a second eligibility rule.
- Events are not work items. They belong in Calendar and hard-date views.
- Pipeline stall logic is already centralized in `preFilingStallDetection`/the server dashboard engine and should not be reimplemented by visual components.
- Fee-shift attention remains deferred to authoritative risk-analysis data. Trial Preparation, an appraisal, or an opinion alone is not sufficient.
- The existing client stack has no charting library. CSS bar lists are adequate for this controlled package and provide a stronger accessible nonvisual equivalent than a canvas chart.

## Proposed layout

Attorney: KPI strip, compact urgency chips/planning row, then short Overdue/Due Soon/Action Queue lists and the existing planning tabs.

Manager: management KPI strip, hard-date/trial summaries, then Needs Attention, Pipeline, By Attorney, Calendar, and data quality. The manager view should not replicate every attorney work reminder.

## Drill-down map

| Summary | Destination |
| --- | --- |
| Attorney Overdue / Due Today / Next 7 Days / urgency bar | Work Queue with matching urgency filter |
| Attorney hard-date bucket | Calendar with matching date range |
| Manager hard dates | Manager Calendar with matching horizon |
| Manager jury trials by attorney | Calendar/event list filtered to Jury Trial and the selected attorney when provider identity supports it |
| Pipeline stall | Division Pipeline / Needs Attention |
| Service risk | Needs Attention / Service view |

## Deferred or rejected

Historical trend dashboards, custom widgets, predictive scores, attorney productivity rankings, opaque workload scores, decorative charts, and a new chart dependency are deferred. Total open cases and total pipeline cases remain supporting context only.

## Tests required for this package

Client tests cover accessible count values, compact planning routing, urgency drill-down, work-row actions, and range selection. Existing server tests cover shared work eligibility, service bands, pipeline aging, and assignment-aware dashboard scope. The next server test additions should assert that management hard-date/trial summaries count records rather than attorney assignment rows.
