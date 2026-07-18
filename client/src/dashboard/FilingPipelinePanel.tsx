import { useState } from 'react'
import type { FilingPipelineView } from './types'
import { FilingPipelineRow } from './FilingPipelineRow'
import { EmptyState } from './EmptyState'

type Tab = 'myDesk' | 'waiting' | 'allPipeline'

export function FilingPipelinePanel({
  pipeline,
  onOpenCase,
  onHandoff,
  onNote,
  onHolder,
  onReview,
  onAdvance,
}: {
  pipeline: FilingPipelineView
  onOpenCase: (caseId: number) => void
  onHandoff: (caseId: number) => void
  onNote?: (caseId: number) => void
  onHolder?: (caseId: number) => void
  onReview?: (caseId: number) => void
  onAdvance?: (caseId: number) => void
}) {
  const [tab, setTab] = useState<Tab>('myDesk')
  const rows = pipeline[tab]

  return (
    <div className="filing-pipeline-panel">
      <div className="chip-row" role="tablist" aria-label="Filing pipeline view">
        <button className={tab === 'myDesk' ? 'chip active' : 'chip'} role="tab" aria-selected={tab === 'myDesk'} onClick={() => setTab('myDesk')}>
          My Desk ({pipeline.myDesk.length})
        </button>
        <button className={tab === 'waiting' ? 'chip active' : 'chip'} role="tab" aria-selected={tab === 'waiting'} onClick={() => setTab('waiting')}>
          Waiting ({pipeline.waiting.length})
        </button>
        <button className={tab === 'allPipeline' ? 'chip active' : 'chip'} role="tab" aria-selected={tab === 'allPipeline'} onClick={() => setTab('allPipeline')}>
          All Pipeline ({pipeline.allPipeline.length})
        </button>
      </div>

      {rows.length === 0 ? (
        <EmptyState
          title={tab === 'myDesk' ? 'Nothing on your desk right now' : 'No matters here'}
          description={tab === 'myDesk' ? 'Pre-filing tracts needing attorney review, revision, or marked rushed will appear here.' : 'Pre-filing tracts will appear here as they move through the pipeline.'}
        />
      ) : (
        <div className="table-wrap">
          <table className="compact-table">
            <thead>
              <tr>
                <th>Tract / Owner</th>
                <th>Project</th>
                <th>County</th>
                <th>Holder</th>
                <th>Date Assigned</th>
                <th>Next Review</th>
                <th>Note / Next Action</th>
                <th>Last Updated</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <FilingPipelineRow key={row.caseId} row={row} onOpenCase={onOpenCase} onHandoff={onHandoff} onNote={onNote ?? (() => {})} onHolder={onHolder ?? (() => {})} onReview={onReview ?? (() => {})} onAdvance={onAdvance ?? (() => {})} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
