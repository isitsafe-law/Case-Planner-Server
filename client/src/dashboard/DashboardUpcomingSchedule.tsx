import type { ReactNode } from 'react'

export type UpcomingScheduleItem = {
  key: string
  date: string
  endDate?: string | null
  kind: 'event'
  type: string
  title: string
  caseId: number
  caseName: string
  daysRemaining: number
  assignedAttorney?: string | null
}

function dateLabel(item: UpcomingScheduleItem): string {
  return item.endDate && item.endDate !== item.date ? `${item.date} – ${item.endDate}` : item.date
}

// `featured` renders as the first row in the same list as the regular schedule items - used by
// DashboardCompactSummaries to fold the "Next jury trial" card into this one panel instead of a
// separate floating card, so the calendar-forward content on the dashboard reads as one prominent
// place to look, not two competing widgets.
export function DashboardUpcomingSchedule({ items, onEvent, onViewCalendar, featured }: {
  items: UpcomingScheduleItem[]
  onEvent: (item: UpcomingScheduleItem) => void
  onViewCalendar: () => void
  featured?: ReactNode
}) {
  return <section className="ui-table-panel dashboard-schedule-card" aria-labelledby="upcoming-schedule-heading">
    <div className="panel-hd"><h3 id="upcoming-schedule-heading">Upcoming Schedule</h3><span className="count">{items.length} shown</span><button type="button" className="link-button" onClick={onViewCalendar}>View Calendar</button></div>
    <div className="dashboard-schedule-list">
      {featured}
      {items.length === 0 ? <p className="dashboard-compact-empty">No other upcoming case events.</p> : items.map((item) => <button key={item.key} type="button" className="dashboard-schedule-item" onClick={() => onEvent(item)} aria-label={`${dateLabel(item)}. ${item.type}. ${item.title}. ${item.caseName}. ${item.daysRemaining} days remaining.`}>
        <span className="dashboard-schedule-date">{dateLabel(item)}<small>{item.daysRemaining === 0 ? 'Today' : `${item.daysRemaining} days`}</small></span>
        <span className="dashboard-schedule-detail"><strong>{item.type}</strong><span>{item.title}</span><small>{item.caseName}{item.assignedAttorney ? ` · ${item.assignedAttorney}` : ''}</small></span>
        <span className="dashboard-schedule-destination">Calendar</span>
      </button>)}
    </div>
  </section>
}
