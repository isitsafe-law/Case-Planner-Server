# Dashboard Layout Audit

The dashboard uses a two-column CSS grid after the full-width Action Queue. The left column is `2fr` and the right column is `1fr` with a 280px minimum. Overdue Work and Due in the Next 7 Days occupy the left stack; the planning wrapper occupies the right stack; Case Insight remains secondary below the planning row. On narrow screens the grid becomes one column in Action Queue, Overdue, Due, planning, Case Insight order.

The right-side width issue came from the nested planning wrapper not explicitly defining a single full-width column. Its child trial card could shrink to content while the schedule used its own available area. The wrapper and both planning cards now explicitly use `minmax(0, 1fr)` and `width: 100%`, with no fixed heights or row spans.

Upcoming Schedule remains event-only and content-driven. Rows use a wider non-wrapping date column and a flexible detail column; the repeated Calendar destination text is hidden because the whole row is clickable. Empty trial, schedule, and seven-day work states remain compact.
