import { Btn } from '../ui/Btn'
import { downloadCsv } from '../ui/csvExport'
import { EmptyState } from './EmptyState'
import { preFilingMilestoneLabel, type PreFilingMilestoneAgingSummary } from './types'

// Read-only: marking a pre-filing milestone records a fact, not a decision, and is already
// available to anyone with the right access from the case workspace - no approve/deny affordance
// belongs here. Consumes the server's already-aggregated GET /api/prefiling-milestones/aging
// response as-is; the furthest-milestone-per-case computation lives server-side
// (PreFilingMilestoneAgingCase), unlike IncomingPipelinePanel.tsx's own client-side scan, which
// serves a different (Calendar tab) purpose. Rendered directly on the manager dashboard's Filing
// Status tab (see ManagerDashboard.tsx) - the Settlement Authority section this used to sit
// alongside was removed as redundant with the Risk Analysis tab's negotiation tracking.
export function FilingStatusSection({
  aging,
  onOpenCase,
}: {
  aging: PreFilingMilestoneAgingSummary | null
  onOpenCase: (caseId: number) => void
}) {
  if (!aging || aging.cases.length === 0) {
    return <EmptyState title="No tracts currently in Pipeline." description="Filing status appears here once a tract enters Pipeline status." />
  }

  function exportCsv() {
    const rows = aging!.cases.map((row) => ({
      'Job Number': row.jobNumber || '',
      Tract: row.tract || '',
      'Case Name': row.caseName || '',
      'Furthest Milestone': row.furthestMilestone === 'None' ? 'No milestones marked yet' : preFilingMilestoneLabel(row.furthestMilestone),
      'Days Since Marked': row.daysSinceMarked ?? '',
    }))
    downloadCsv(`Filing_Status_${new Date().toISOString().slice(0, 10)}.csv`, rows)
  }

  return (
    <div>
      <div className="ui-tiles" style={{ marginBottom: '0.85rem', flexWrap: 'wrap' }}>
        {aging.buckets.map((bucket) => (
          <div key={bucket.milestone} className="ui-tile">
            <span className="ui-tile-label">{bucket.milestone === 'None' ? 'No milestones marked yet' : preFilingMilestoneLabel(bucket.milestone)}</span>
            <span className="ui-tile-value">{bucket.count}</span>
          </div>
        ))}
      </div>
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: '0.6rem' }}>
        <Btn onClick={exportCsv}>Export CSV</Btn>
      </div>
      <div className="table-wrap">
        <table className="ui-table compact-table">
          <thead>
            <tr>
              <th>Job + Tract</th>
              <th>Case Name</th>
              <th>Furthest Milestone</th>
              <th>Days at This Milestone</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {aging.cases.map((row) => (
              <tr key={row.caseId}>
                <td>{[row.jobNumber, row.tract].filter(Boolean).join(' · ') || '—'}</td>
                <td>{row.caseName || '—'}</td>
                <td>{row.furthestMilestone === 'None' ? 'No milestones marked yet' : preFilingMilestoneLabel(row.furthestMilestone)}</td>
                <td>{row.daysSinceMarked ?? '—'}</td>
                <td><Btn size="sm" onClick={() => onOpenCase(row.caseId)}>Open Case</Btn></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
