import { Btn } from '../ui/Btn'

export type DashboardBar = {
  key: string
  label: string
  count: number
  detail?: string
}

function BarList({ title, description, bars, onSelect, emptyText }: {
  title: string
  description: string
  bars: DashboardBar[]
  onSelect?: (bar: DashboardBar) => void
  emptyText: string
}) {
  const max = Math.max(1, ...bars.map((bar) => bar.count))
  return (
    <section className="dashboard-visual-panel" aria-label={title}>
      <div className="dashboard-visual-heading"><div><h3>{title}</h3><p>{description}</p></div></div>
      {bars.every((bar) => bar.count === 0) ? <p className="dashboard-visual-empty">{emptyText}</p> : (
        <div className="dashboard-bar-list">
          {bars.map((bar) => (
            <button key={bar.key} className="dashboard-bar-row" onClick={() => onSelect?.(bar)} disabled={!onSelect || bar.count === 0} aria-label={`${bar.label}: ${bar.count}${bar.detail ? `. ${bar.detail}` : ''}`}>
              <span className="dashboard-bar-label">{bar.label}</span>
              <span className="dashboard-bar-track" aria-hidden="true"><span style={{ width: `${(bar.count / max) * 100}%` }} /></span>
              <strong className="dashboard-bar-count">{bar.count}</strong>
              {bar.detail && <span className="dashboard-bar-detail">{bar.detail}</span>}
            </button>
          ))}
        </div>
      )}
    </section>
  )
}

export function DashboardVisualSummaries({
  urgency,
  hardDates,
  onUrgency,
  onHardDates,
}: {
  urgency: DashboardBar[]
  hardDates: DashboardBar[]
  onUrgency: (bar: DashboardBar) => void
  onHardDates: (bar?: DashboardBar) => void
}) {
  return (
    <div className="dashboard-visual-grid">
      <BarList title="Work by urgency" description="Incomplete work from the same actionable-work feed used by Work Queue." bars={urgency} onSelect={onUrgency} emptyText="No dated work is currently open." />
      <BarList title="Upcoming hard dates" description="Jury trials, proceedings, and court deadlines within the next 180 days." bars={hardDates} onSelect={onHardDates} emptyText="No hard dates are scheduled within the next 180 days." />
      <div className="dashboard-visual-actions"><Btn size="sm" variant="ghost" onClick={() => onHardDates()}>Open Calendar for all hard dates</Btn></div>
    </div>
  )
}
