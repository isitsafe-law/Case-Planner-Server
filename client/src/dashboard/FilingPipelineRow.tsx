import type { PreFilingTractRow } from './types'
import { formatDate } from '../ui/format'

export function FilingPipelineRow({
  row,
  onOpenCase,
  onHandoff,
  onNote,
  onHolder,
  onReview,
  onAdvance,
}: {
  row: PreFilingTractRow
  onOpenCase: (caseId: number) => void
  onHandoff: (caseId: number) => void
  onNote?: (caseId: number) => void
  onHolder?: (caseId: number) => void
  onReview?: (caseId: number) => void
  onAdvance?: (caseId: number) => void
}) {
  return (
    <tr className={row.priority !== 'Normal' ? 'filing-pipeline-row priority' : 'filing-pipeline-row'}>
      <td>
        {row.tractOrOwnerName}
        {row.priority !== 'Normal' && <span className="pill pill-warn"> {row.priority}</span>}
      </td>
      <td>{row.projectName ?? row.jobNumber ?? '-'}</td>
      <td>{row.county ?? '-'}</td>
      <td>{row.currentHolder ?? <span className="pill pill-danger">Missing</span>}</td>
      <td>{formatDate(row.dateSentToCurrentHolder)}</td>
      <td>{formatDate(row.nextReviewDate)}</td>
      <td>{row.currentIssue ?? row.flagReason ?? '-'}</td>
      <td>{formatDate(row.lastUpdated)}</td>
      <td>
        <div className="button-row compact-actions row-actions">
          <button className="primary" onClick={() => onOpenCase(row.caseId)}>Open Case</button>
          <details className="pipeline-more-actions"><summary>More</summary><div className="pipeline-more-menu">
            <button onClick={() => onHolder?.(row.caseId)}>Change Holder</button>
            <button onClick={() => onReview?.(row.caseId)}>Set Next Review</button>
            <button onClick={() => onNote?.(row.caseId)}>Add Note</button>
            <button onClick={() => onHandoff(row.caseId)}>Hand off</button>
            <button onClick={() => onAdvance?.(row.caseId)}>Advance to Filed</button>
          </div></details>
        </div>
      </td>
    </tr>
  )
}
