import { useMemo } from 'react'
import type { CaseRecord } from '../App'
import { PRE_FILING_MILESTONE_ORDER, preFilingMilestoneLabel, type PreFilingMilestoneRecord } from './types'
import { EmptyState } from './EmptyState'
import { Btn } from '../ui/Btn'
import { downloadCsv } from '../ui/csvExport'

type PipelineTractRow = {
  caseId: number
  jobNumber: string
  tract: string
  currentHolder: string
  subState: string
  dateSentToCurrentHolder: string | null
}

// The furthest (highest-order) marked milestone for a case, per the fixed 4-milestone order - or
// "No milestones marked yet" when none are marked. Pipeline tracts having no court case
// number/division yet is normal (pre-filing); this label never implies anything is missing/wrong.
function furthestMilestoneLabel(caseId: number, milestonesByCase: Map<number, PreFilingMilestoneRecord[]>): string {
  const rows = milestonesByCase.get(caseId) || []
  let furthestIndex = -1
  for (const row of rows) {
    if (!row.isMarked) continue
    const idx = PRE_FILING_MILESTONE_ORDER.indexOf(row.milestone as (typeof PRE_FILING_MILESTONE_ORDER)[number])
    if (idx > furthestIndex) furthestIndex = idx
  }
  return furthestIndex === -1 ? 'No milestones marked yet' : preFilingMilestoneLabel(PRE_FILING_MILESTONE_ORDER[furthestIndex])
}

export function IncomingPipelinePanel({
  allCases,
  preFilingMilestones,
  onOpenCase,
}: {
  allCases: CaseRecord[]
  preFilingMilestones: PreFilingMilestoneRecord[]
  onOpenCase: (caseId: number) => void
}) {
  const milestonesByCase = useMemo(() => {
    const map = new Map<number, PreFilingMilestoneRecord[]>()
    for (const milestone of preFilingMilestones) {
      if (!map.has(milestone.caseId)) map.set(milestone.caseId, [])
      map.get(milestone.caseId)!.push(milestone)
    }
    return map
  }, [preFilingMilestones])

  const groups = useMemo(() => {
    const pipelineCases = allCases.filter((record) => (record.caseStatus || 'Pipeline') === 'Pipeline')
    const byAttorney = new Map<string, PipelineTractRow[]>()
    for (const record of pipelineCases) {
      const attorney = record.assignedAttorney || 'Unassigned'
      const row: PipelineTractRow = {
        caseId: record.id,
        jobNumber: record.jobNumber || '',
        tract: record.tract || '',
        currentHolder: record.currentHolder || 'Unassigned',
        subState: `${record.currentHolder || 'Unassigned'} · ${furthestMilestoneLabel(record.id, milestonesByCase)}`,
        dateSentToCurrentHolder: record.dateSentToCurrentHolder || null,
      }
      if (!byAttorney.has(attorney)) byAttorney.set(attorney, [])
      byAttorney.get(attorney)!.push(row)
    }
    // Within each attorney group: sort by dateSentToCurrentHolder ascending (oldest handoff date
    // first - the tract that has been sitting the longest surfaces at the top of its attorney's
    // list, which is the more actionable ordering for a manager scanning for stalled tracts).
    // Tracts with no recorded handoff date sort after every dated tract, by job number.
    for (const rows of byAttorney.values()) {
      rows.sort((a, b) => {
        if (a.dateSentToCurrentHolder && b.dateSentToCurrentHolder) return a.dateSentToCurrentHolder.localeCompare(b.dateSentToCurrentHolder)
        if (a.dateSentToCurrentHolder) return -1
        if (b.dateSentToCurrentHolder) return 1
        return a.jobNumber.localeCompare(b.jobNumber)
      })
    }
    // Attorney groups alphabetical, with the Unassigned bucket pinned last.
    return Array.from(byAttorney.entries()).sort((a, b) => {
      if (a[0] === 'Unassigned') return 1
      if (b[0] === 'Unassigned') return -1
      return a[0].localeCompare(b[0])
    })
  }, [allCases, milestonesByCase])

  const totalTracts = groups.reduce((sum, [, rows]) => sum + rows.length, 0)

  function exportCsv() {
    const rows = groups.flatMap(([attorney, tracts]) => tracts.map((tract) => ({
      Attorney: attorney,
      'Job Number': tract.jobNumber,
      Tract: tract.tract,
      'Current Holder': tract.currentHolder,
      'Pipeline Sub-state': tract.subState,
    })))
    downloadCsv(`Incoming_Pipeline_${new Date().toISOString().slice(0, 10)}.csv`, rows)
  }

  if (totalTracts === 0) {
    return <EmptyState title="No tracts currently in Pipeline." description="Tracts appear here before the Complaint in Condemnation is filed." />
  }

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: '0.6rem' }}>
        <Btn onClick={exportCsv}>Export CSV</Btn>
      </div>
      <p className="helper-text" style={{ marginBottom: '0.85rem' }}>
        Tracts without a recorded handoff date are sorted by job number.
      </p>
      {groups.map(([attorney, rows]) => (
        <div key={attorney} className="top-gap-small">
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.35rem' }}>
            <h4 style={{ margin: 0 }}>{attorney}</h4>
            <span className="pill pill-neutral">{rows.length} tract{rows.length === 1 ? '' : 's'}</span>
          </div>
          <div className="table-wrap">
            <table className="ui-table compact-table">
              <thead>
                <tr><th>Job + Tract</th><th>Holder · Furthest Milestone</th><th></th></tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.caseId}>
                    <td>{[row.jobNumber, row.tract].filter(Boolean).join(' · ') || '—'}</td>
                    <td>{row.subState}</td>
                    <td><Btn size="sm" onClick={() => onOpenCase(row.caseId)}>Open Case</Btn></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      ))}
    </div>
  )
}
