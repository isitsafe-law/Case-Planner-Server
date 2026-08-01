# Dashboard Work Visibility Audit

## Findings

### Work Queue and dashboard sources

- The Work Queue page loads the raw work feeds from `/api/work-queues/deadlines`, `/api/work-queues/checklist`, `/api/work-queues/discovery`, and `/api/work-queues/service`.
- The dashboard loaded a separate `/api/dashboard/upcoming-work` projection and then applied another client-side filter.
- The projection reused the same underlying deadline, checklist, discovery, and service records, but it excluded every non-service item on Pipeline cases and the SQLite implementation capped the result at ten rows. Those two differences explained why work visible in Work Queue could disappear from the dashboard.
- Events are loaded from `/api/work-queues/hearings` and are intentionally excluded from actionable work; the Calendar owns event visibility.

### Date behavior

Dates are stored and compared as date-only `yyyy-MM-dd` values. The shared work rules now classify them using the application’s local `DateOnly.FromDateTime(DateTime.Today)` boundary:

- Overdue: due date before today.
- Due in the next seven days: today through today plus seven calendar days, inclusive.
- The two windows do not overlap.

The dashboard no longer treats overdue work as part of the next-seven-day panel.

### Ownership and visibility

The endpoint remains protected by `CaseAccessService` visible-case scope. Local SQLite mode has no authenticated Entra identity, so it exposes the visible local docket for testing. Item-specific checklist assignment and discovery assignment remain available for the future authenticated ownership layer; the current dashboard does not invent a second assignment filter or duplicate work for supporting attorneys.

### Action Queue explanations

The row previously displayed `reason` in the main table and repeated the same value under “Why this is here.” The main row now displays the reason once. A distinct posture summary, policy threshold, and recommended action may still appear below it when they add information.

### Look-ahead settings

- Discovery cutoff look-ahead watches the recorded discovery cutoff on an incomplete discovery posture. It changes when the case appears in attention views; it does not create the cutoff.
- Trial preparation look-ahead watches the jury-trial date for the active preparation window. It changes visibility and preparation signals; it does not change the trial date or automatically create tasks.
- Trial watch look-ahead is the earlier awareness window for trial-track matters used for staffing, scheduling, and planning. It is distinct from the closer preparation window and does not create tasks.

### Fee-shift attention

The old Division Overview row was generated solely when a case reached Trial Preparation and had a deposit amount. That was only a reference calculation, not a fee-shift condition, and it caused every qualifying Trial Preparation case to look like a fee-shift exception.

The row has been removed from Needs Attention until the authoritative risk-analysis comparison values are available to that view. A valid future rule must require the deposit, a comparison value or result, the documented statutory threshold, and an active case. Missing comparison data must not be presented as confirmed exposure.

## Implemented consistency rules

`ActionableWorkQueryRules` is now the shared server-side definition used by both SQLite and SQL Server dashboard work projections. It owns open-case eligibility, deferred-case exclusion, incomplete item status, date parsing, urgency classification, and inclusive date-window matching. The dashboard requests the full eligible projection and separates overdue from today-through-seven-days locally, while Work Queue remains the complete operational list.

## Remaining limitation

The local SQLite build cannot resolve a logged-in attorney identity until Entra is enabled. Per-attorney and supporting-attorney ownership rules are therefore preserved as a future authorization layer rather than guessed from names in the preview build.
