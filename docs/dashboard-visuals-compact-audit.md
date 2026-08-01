# Dashboard Visuals and Work-Row Compactness Audit

Status: implemented baseline, 2026-08-01

## Findings

1. Attorney **Work by Urgency** and **Upcoming Hard Dates** are rendered by `DashboardVisualSummaries`. They are populated from the shared `/api/dashboard/upcoming-work` projection plus client-loaded hearing/deadline feeds.
2. Division Overview visuals are rendered by `ManagerDashboardVisuals`. Its bars are derived from the manager's loaded cases, hearings, deadlines, and pre-filing aging response.
3. The panels reserve a two-column grid, card padding, explanatory text, one row per bucket, and a full-width action row. On narrow screens they stack, so two panels become a long vertical section.
4. The bar lengths have no useful denominator. The meaningful values are the independent counts; a count of four should not appear as a nearly full bar.
5. Attorney hard-date bars combine qualifying events and open deadlines. Their prior click route opened Calendar for every bucket, which was incorrect for deadlines.
6. Work Queue already accepts urgency filters including Overdue, Due Today, Due in 7 Days, Due in 8–30 Days, Due in 14 Days, and Due in 30 Days. Its `deadlines` facet is the correct deadline destination.
7. Calendar already accepts date range, event type, and attorney scope. It is the correct destination for jury trials and other case events.
8. Overdue and Due Soon already provide the actual records and quick actions, so the large urgency bars duplicate those lists.
9. `DashboardWorkActions` currently renders the type-specific primary action, due-date editor, and Open action in a wrapping action group. The dashboard rows also have a separate Open button in Due Soon, producing excess width and inconsistent controls.
10. The primary action is completion/response/service update. Change Due Date and open-item/open-case actions are secondary. Case names can safely become case-opening buttons because `openCase` already accepts the case ID and destination tab.

## Recommendation

- Remove both large attorney bar panels and the manager bar panels.
- Retain the useful information as compact count controls: urgency counts, jury trials, events, and deadlines. Counts are buttons, not proportional bars.
- Add a short planning row showing the next jury trial and compact event/deadline counts.
- Route event counts to Calendar and deadline counts to Work Queue's deadline facet. Mixed summaries must have separate controls.
- Keep Action Queue first, followed by actionable Overdue and Due Soon lists as the main dashboard body.
- Keep the manager KPI strip; remove the manager bar charts and leave exception lists/tabs as the detail destination.
- Make case names clickable and keep only the primary action visible in each row. Secondary actions remain available through a compact overflow menu.

The implementation now uses a responsive attorney card grid: Action Queue is first, followed by Overdue Work, Due Soon, and a compact planning column containing Next Jury Trial above an event-only Upcoming Schedule. Upcoming Schedule shows up to five actual case events with date, type, case, days remaining, and Calendar routing. KPI tiles use short explanatory hints; no attorney dashboard chart or urgency-chip row remains.

## Layout and accessibility

Desktop rows use type, title, case link, due information, and a compact action group. Narrow rows wrap details but retain the primary action and a labelled secondary-actions menu. All count controls are keyboard-focusable buttons with text labels and counts; color is supplemental only.

## Tests

Update visual-summary tests to assert bar panels are absent, compact controls route by category, zero counts do not create large rows, and event/deadline destinations remain distinct. Add work-row tests for the visible primary action, secondary menu, case-link activation, due-date change, and keyboard menu access.
