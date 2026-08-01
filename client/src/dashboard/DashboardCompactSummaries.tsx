export type DashboardBar = { key: string; label: string; count: number; detail?: string }

export type PlanningSummary = {
  juryTrials: number
  events: number
  deadlines: number
  nextJuryTrial?: { date: string; caseName: string } | null
}

function CountButton({ label, count, onClick }: { label: string; count: number; onClick: () => void }) {
  return <button type="button" className="dashboard-count-chip" onClick={onClick} aria-label={`${label}: ${count}`}><span>{label}</span><strong>{count}</strong></button>
}

export function DashboardCompactSummaries({ urgency, planning, onUrgency, onJuryTrials, onEvents, onDeadlines }: {
  urgency: DashboardBar[]
  planning: PlanningSummary
  onUrgency: (bar: DashboardBar) => void
  onJuryTrials: () => void
  onEvents: () => void
  onDeadlines: () => void
}) {
  return <div className="dashboard-compact-summary">
    <div className="dashboard-compact-row" aria-label="Work by urgency">
      {urgency.map((bar) => <CountButton key={bar.key} label={bar.label} count={bar.count} onClick={() => onUrgency(bar)} />)}
    </div>
    <div className="dashboard-planning-row" aria-label="Upcoming planning summary">
      <button type="button" className="dashboard-planning-card dashboard-planning-card-wide" onClick={onJuryTrials}>
        <span>Next jury trial</span>
        <strong>{planning.nextJuryTrial ? planning.nextJuryTrial.date : 'None scheduled'}</strong>
        <small>{planning.nextJuryTrial?.caseName || `${planning.juryTrials} within 180 days`}</small>
      </button>
      <CountButton label="Events · 30 days" count={planning.events} onClick={onEvents} />
      <CountButton label="Deadlines · 30 days" count={planning.deadlines} onClick={onDeadlines} />
    </div>
  </div>
}
