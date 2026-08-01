# Dashboard Priority Audit

## Baseline findings

- The Action Queue is rendered before the other dashboard panels in JSX, but its CSS placed it in a full-width third row. This made the most actionable list visually secondary; the empty state did not change that grid position.
- The current top KPI row is driven by `attorneyDashboard.actionQueue` priority levels: Immediate (1), Attorney decision (2), Momentum (3), and Planned work (4). The queue is one row per case and uses the lowest-numbered signal as its primary reason.
- Immediate is currently the queue's priority-1 bucket. The shared work projection separately contains overdue and due-today work; the final KPI will use that projection for a direct Work Queue match.
- Attorney decision is the queue's priority-2 bucket, principally unselected discovery strategy and substantive discovery decisions/reviews.
- Momentum is not a single business event. It currently covers: no meaningful litigation activity for the configured stale threshold; a waiting follow-up date that passed; a waiting record missing its follow-up date; and a filed case with neither a next action/review date nor a waiting record. These signals are currently priority 3. Valid review/escalation signals remain in the Action Queue; vague inactivity is not promoted to a replacement KPI.
- Planned work is currently priority 4: trial/court-event preparation and trial-preparation window signals. It is not a complete 8–30-day work projection and therefore should not remain under that name as a date-based KPI. The final top row uses an explicitly date-based Upcoming Work count instead.
- Jury Trials is based on open, active Jury Trial events plus legacy case trial dates within 180 days, with one case-level count per source and the nearest trial selected for the supporting card.

## Final dashboard contract

1. Action Queue
2. Overdue Work
3. Due in the Next 7 Days
4. Compact Next Jury Trial followed by Upcoming Schedule
5. Collapsible Case Insight

The top KPI row is: Immediate, Attorney Decision, Upcoming Work (open tasks/deadlines/checklist-style work due 8–30 days), and Jury Trials (within 180 days). Momentum is removed from the user interface. The old urgency-range chip row is removed; detailed range filtering remains in Work Queue.

Upcoming Schedule reads the authoritative `queueHearings` event source, includes active case events in chronological order, includes currently active multi-day events, excludes deadlines/tasks/checklist items, and omits the same event selected for Next Jury Trial.

