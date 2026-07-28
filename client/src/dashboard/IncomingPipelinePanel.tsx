import { useMemo, useState } from 'react'
import type { CaseRecord } from '../App'
import { api } from '../App'
import type { PreFilingMilestone, PreFilingMilestoneRecord, ReviewNoteRecord } from './types'
import { preFilingMilestoneLabel } from './types'
import { computePreFilingStallInfo } from './preFilingStallDetection'
import { EmptyState } from './EmptyState'
import { Btn } from '../ui/Btn'
import { downloadCsv } from '../ui/csvExport'

type PipelineTractRow = {
  caseId: number
  jobNumber: string
  tract: string
  currentHolder: string
  subState: string
  isReturnedForRevision: boolean
  nextMilestone: PreFilingMilestone | null
  dateSentToCurrentHolder: string | null
}

function todayIsoDate(): string {
  return new Date().toISOString().slice(0, 10)
}

export function IncomingPipelinePanel({
  allCases,
  preFilingMilestones,
  reviewNotes,
  onOpenCase,
  onMutated,
}: {
  allCases: CaseRecord[]
  preFilingMilestones: PreFilingMilestoneRecord[]
  reviewNotes: ReviewNoteRecord[]
  onOpenCase: (caseId: number) => void
  // Final implementation, item 1c: lets a manager mark directly from an aging row without
  // navigating to the case - refreshes preFilingMilestones (and everything the shared stall
  // detector depends on) after a successful inline mark, same as BulkMilestoneGrid.tsx's onMutated.
  onMutated?: () => Promise<void>
}) {
  const [markingCaseId, setMarkingCaseId] = useState<number | null>(null)
  const [occurredDate, setOccurredDate] = useState(todayIsoDate())
  const [busy, setBusy] = useState(false)
  const [errorMessage, setErrorMessage] = useState('')

  const groups = useMemo(() => {
    const pipelineCases = allCases.filter((record) => (record.caseStatus || 'Pipeline') === 'Pipeline')
    const byAttorney = new Map<string, PipelineTractRow[]>()
    for (const record of pipelineCases) {
      const attorney = record.assignedAttorney || 'Unassigned'
      // Shared with NeedsAttentionTab.tsx's preFilingStallRow - the SAME detector, so this panel's
      // "what's this tract waiting on" always matches the reason it would show up in Needs
      // Attention, never a separately-derived label (final implementation, item 3).
      const stallInfo = computePreFilingStallInfo(record.id, preFilingMilestones, reviewNotes)
      const row: PipelineTractRow = {
        caseId: record.id,
        jobNumber: record.jobNumber || '',
        tract: record.tract || '',
        currentHolder: record.currentHolder || 'Unassigned',
        subState: `${record.currentHolder || 'Unassigned'} · ${stallInfo.label}`,
        isReturnedForRevision: stallInfo.isReturnedForRevision,
        nextMilestone: stallInfo.nextMilestone,
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
  }, [allCases, preFilingMilestones, reviewNotes])

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

  async function markInline(caseId: number, milestone: PreFilingMilestone) {
    setBusy(true)
    try {
      setErrorMessage('')
      await api(`/api/cases/${caseId}/prefiling-milestones/${milestone}/mark`, {
        method: 'POST',
        body: JSON.stringify({ occurredDate }),
      })
      setMarkingCaseId(null)
      await onMutated?.()
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to mark the milestone.')
    } finally {
      setBusy(false)
    }
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
      {errorMessage && <p className="helper-text" style={{ color: 'var(--danger, #b3261e)' }}>{errorMessage}</p>}
      {groups.map(([attorney, rows]) => (
        <div key={attorney} className="top-gap-small">
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.35rem' }}>
            <h4 style={{ margin: 0 }}>{attorney}</h4>
            <span className="pill pill-neutral">{rows.length} tract{rows.length === 1 ? '' : 's'}</span>
          </div>
          <div className="table-wrap">
            <table className="ui-table compact-table">
              <thead>
                <tr><th>Job + Tract</th><th>Holder · Pre-Filing Status</th><th></th></tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.caseId}>
                    <td>{[row.jobNumber, row.tract].filter(Boolean).join(' · ') || '—'}</td>
                    <td>
                      {row.subState}
                      {row.isReturnedForRevision && <span className="pill pill-warn" style={{ marginLeft: '0.4rem' }}>Returned</span>}
                    </td>
                    <td>
                      <div className="button-row compact-actions">
                        {row.nextMilestone && onMutated && (
                          markingCaseId === row.caseId ? (
                            <span className="button-row compact-actions">
                              <input type="date" value={occurredDate} onChange={(event) => setOccurredDate(event.currentTarget.value)} />
                              <Btn size="sm" disabled={busy} onClick={() => void markInline(row.caseId, row.nextMilestone!)}>Confirm</Btn>
                              <Btn size="sm" variant="ghost" onClick={() => setMarkingCaseId(null)}>Cancel</Btn>
                            </span>
                          ) : (
                            <Btn size="sm" variant="ghost" onClick={() => { setOccurredDate(todayIsoDate()); setMarkingCaseId(row.caseId) }}>
                              Mark {preFilingMilestoneLabel(row.nextMilestone)}…
                            </Btn>
                          )
                        )}
                        <Btn size="sm" onClick={() => onOpenCase(row.caseId)}>Open Case</Btn>
                      </div>
                    </td>
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
