import type { FilingPipelineView } from './types'
import { EmptyState } from './EmptyState'

// Plain calendar-day difference (matches the server's daysSinceMeaningfulActivity convention in
// AttorneyDashboardEngine.cs: Math.Max(0, today.DayNumber - lastDate.DayNumber)) - floored at 0
// since a case can't be "with" a holder for a negative number of days.
function daysWithHolder(dateSentToCurrentHolder: string | null): number | null {
  if (!dateSentToCurrentHolder) return null
  const match = dateSentToCurrentHolder.match(/^(\d{4})-(\d{2})-(\d{2})/)
  if (!match) return null
  const sentUtc = Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]))
  const today = new Date()
  const todayUtc = Date.UTC(today.getFullYear(), today.getMonth(), today.getDate())
  return Math.max(0, Math.round((todayUtc - sentUtc) / 86_400_000))
}

export function FilingPipelinePanel({
  pipeline,
  onOpenCase,
  onHandoff,
}: {
  pipeline: FilingPipelineView
  onOpenCase: (caseId: number) => void
  onHandoff: (caseId: number) => void
}) {
  const rows = pipeline.allPipeline

  if (rows.length === 0) {
    return <EmptyState title="No pre-filing matters right now" description="Pre-filing tracts needing attorney review, revision, or filing will appear here." />
  }

  return (
    <div className="pipeline-watch-list">
      {rows.map((row) => {
        const days = daysWithHolder(row.dateSentToCurrentHolder)
        const priorityTone = row.priority === 'Rushed' ? 'pill-danger' : row.priority === 'Priority' ? 'pill-warn' : null
        return (
          <article key={row.caseId} className="pipeline-watch-card" onClick={() => onOpenCase(row.caseId)}>
            <div className="pipeline-watch-card-header">
              <div><strong>{row.tractOrOwnerName}</strong></div>
              {priorityTone && <span className={`pill ${priorityTone}`}>{row.priority}</span>}
            </div>
            <div className="pipeline-watch-primary">
              <div><span>Current holder</span><strong>{row.currentHolder || 'Unassigned'}</strong></div>
              <div><span>Days with holder</span><strong>{days ?? '—'}</strong></div>
            </div>
            <div className="button-row">
              <button onClick={(event) => { event.stopPropagation(); onOpenCase(row.caseId) }}>Open Case</button>
              <button onClick={(event) => { event.stopPropagation(); onHandoff(row.caseId) }}>Hand off</button>
            </div>
          </article>
        )
      })}
    </div>
  )
}
