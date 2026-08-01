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

export function DashboardUpcomingSchedule({ items, onEvent, onViewCalendar }: {
  items: UpcomingScheduleItem[]
  onEvent: (item: UpcomingScheduleItem) => void
  onViewCalendar: () => void
}) {
  return <section className="ui-table-panel dashboard-schedule-card" aria-labelledby="upcoming-schedule-heading">
    <div className="panel-hd"><h3 id="upcoming-schedule-heading">Upcoming Schedule</h3><span className="count">{items.length} shown</span><button type="button" className="link-button" onClick={onViewCalendar}>View Calendar</button></div>
    {items.length === 0 ? <p className="dashboard-compact-empty">No upcoming case events.</p> : <div className="dashboard-schedule-list">{items.map((item) => <button key={item.key} type="button" className="dashboard-schedule-item" onClick={() => onEvent(item)} aria-label={`${dateLabel(item)}. ${item.type}. ${item.title}. ${item.caseName}. ${item.daysRemaining} days remaining.`}>
      <span className="dashboard-schedule-date">{dateLabel(item)}<small>{item.daysRemaining === 0 ? 'Today' : `${item.daysRemaining} days`}</small></span>
      <span className="dashboard-schedule-detail"><strong>{item.type}</strong><span>{item.title}</span><small>{item.caseName}{item.assignedAttorney ? ` · ${item.assignedAttorney}` : ''}</small></span>
      <span className="dashboard-schedule-destination">Calendar</span>
    </button>)}</div>}
  </section>
}
