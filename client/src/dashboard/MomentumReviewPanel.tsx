import type { MomentumReviewEntry } from './types'
import { EmptyState } from './EmptyState'
import { formatDate } from '../ui/format'

const STATUS_TONE: Record<string, string> = {
  Moving: 'success',
  'Waiting Appropriately': 'neutral',
  'Review Required': 'warn',
  Stalled: 'danger',
}

export function MomentumReviewPanel({ entries, onOpenCase }: { entries: MomentumReviewEntry[]; onOpenCase: (caseId: number) => void }) {
  const needsAttention = entries.filter((e) => e.momentumStatus === 'Stalled' || e.momentumStatus === 'Review Required')

  if (needsAttention.length === 0) {
    return <EmptyState title="No cases need a momentum review" description="Cases appear here after 60 days without meaningful activity, or when a waiting follow-up date has passed." />
  }

  return (
    <div className="table-wrap">
      <table className="compact-table">
        <thead>
          <tr>
            <th>Case</th>
            <th>Status</th>
            <th>Days since activity</th>
            <th>Waiting on</th>
            <th>Follow-up</th>
          </tr>
        </thead>
        <tbody>
          {needsAttention.map((e) => (
            <tr key={e.caseId} className="clickable-row" onClick={() => onOpenCase(e.caseId)}>
              <td><button className="ui-case-link" onClick={(event) => { event.stopPropagation(); onOpenCase(e.caseId) }}>{e.caseName}{e.caseNumber ? ` (${e.caseNumber})` : ''}</button></td>
              <td><span className={`pill pill-${STATUS_TONE[e.momentumStatus] ?? 'neutral'}`}>{e.momentumStatus}</span></td>
              <td>{e.daysSinceMeaningfulActivity}</td>
              <td>{e.waitingOn ?? '-'}</td>
              <td>{formatDate(e.waitingFollowUpDate)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
