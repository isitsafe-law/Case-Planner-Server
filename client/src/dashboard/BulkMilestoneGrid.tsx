import { useMemo, useState } from 'react'
import type { CaseRecord } from '../App'
import { api } from '../App'
import { Btn } from '../ui/Btn'
import { EmptyState } from './EmptyState'
import { PRE_FILING_MILESTONE_ORDER, preFilingMilestoneLabel, type PreFilingMilestone, type PreFilingMilestoneRecord } from './types'

// Final implementation, item 1a: the PRIMARY entry point for pre-filing milestone data, matching
// the speed of the spreadsheet it replaces - the Chief Counsel signs one pleadings package covering
// every tract on a job at once, so this grid is scoped to a job number by default, not a per-case
// form. Tracts are rows, the four milestones are columns; selecting tracts under a not-yet-markable
// column and submitting once marks all of them with a single occurred-on date, sharing one BatchId
// (POST /api/prefiling-milestones/bulk-mark) - a tract that can't legally take the mark yet (its
// prerequisite isn't satisfied) is reported as a failure without blocking the rest of the batch.

type BulkMarkFailure = { caseId: number; error: string }
type BulkMarkResult = { batchId: string; marked: PreFilingMilestoneRecord[]; failures: BulkMarkFailure[] }

function todayIsoDate(): string {
  return new Date().toISOString().slice(0, 10)
}

export function BulkMilestoneGrid({
  allCases,
  preFilingMilestones,
  onMutated,
}: {
  allCases: CaseRecord[]
  preFilingMilestones: PreFilingMilestoneRecord[]
  onMutated: () => Promise<void>
}) {
  const [jobNumber, setJobNumber] = useState('')
  const [searchedJobNumber, setSearchedJobNumber] = useState('')
  const [selected, setSelected] = useState<Record<PreFilingMilestone, Set<number>>>({
    PleadingsPackageSent: new Set(),
    ChiefCounselSignaturesReceived: new Set(),
    DeclarationOfTakingSentToDirector: new Set(),
    DirectorSignatureReceived: new Set(),
  })
  const [occurredDate, setOccurredDate] = useState(todayIsoDate())
  const [note, setNote] = useState('')
  const [activeColumn, setActiveColumn] = useState<PreFilingMilestone | null>(null)
  const [busy, setBusy] = useState(false)
  const [errorMessage, setErrorMessage] = useState('')
  const [lastResult, setLastResult] = useState<BulkMarkResult | null>(null)

  const milestonesByCase = useMemo(() => {
    const map = new Map<number, Map<string, PreFilingMilestoneRecord>>()
    for (const record of preFilingMilestones) {
      if (!map.has(record.caseId)) map.set(record.caseId, new Map())
      map.get(record.caseId)!.set(record.milestone, record)
    }
    return map
  }, [preFilingMilestones])

  const tracts = useMemo(() => {
    if (!searchedJobNumber) return []
    return allCases
      .filter((record) => (record.caseStatus || 'Pipeline') === 'Pipeline' && (record.jobNumber || '').trim().toLowerCase() === searchedJobNumber.trim().toLowerCase())
      .sort((a, b) => (a.tract || '').localeCompare(b.tract || ''))
  }, [allCases, searchedJobNumber])

  function canMark(caseId: number, milestone: PreFilingMilestone): boolean {
    const index = PRE_FILING_MILESTONE_ORDER.indexOf(milestone)
    if (index <= 0) return true
    const prerequisite = PRE_FILING_MILESTONE_ORDER[index - 1]
    return Boolean(milestonesByCase.get(caseId)?.get(prerequisite)?.isMarked)
  }

  function isMarked(caseId: number, milestone: PreFilingMilestone): boolean {
    return Boolean(milestonesByCase.get(caseId)?.get(milestone)?.isMarked)
  }

  function toggleSelected(milestone: PreFilingMilestone, caseId: number) {
    setSelected((current) => {
      const next = new Set(current[milestone])
      if (next.has(caseId)) next.delete(caseId)
      else next.add(caseId)
      return { ...current, [milestone]: next }
    })
  }

  function selectedCount(milestone: PreFilingMilestone): number {
    return selected[milestone].size
  }

  async function markSelected(milestone: PreFilingMilestone) {
    const caseIds = Array.from(selected[milestone])
    if (caseIds.length === 0) return
    setBusy(true)
    try {
      setErrorMessage('')
      setLastResult(null)
      const result = await api<BulkMarkResult>('/api/prefiling-milestones/bulk-mark', {
        method: 'POST',
        body: JSON.stringify({ caseIds, milestone, occurredDate, note: note.trim() || undefined }),
      })
      setLastResult(result)
      setSelected((current) => ({ ...current, [milestone]: new Set() }))
      setNote('')
      setActiveColumn(null)
      await onMutated()
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to mark the selected tracts.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <p className="helper-text">
        Scoped to one job number by default, since the Chief Counsel signs one pleadings package covering every tract on a job at once.
        Select tracts under a milestone column and mark them all with one date in a single action.
      </p>
      <div className="inline-quick-form top-gap-small">
        <label>
          <span>Job number</span>
          <input
            value={jobNumber}
            onChange={(event) => setJobNumber(event.currentTarget.value)}
            onKeyDown={(event) => { if (event.key === 'Enter') setSearchedJobNumber(jobNumber) }}
            placeholder="e.g. 012345"
          />
        </label>
        <Btn onClick={() => setSearchedJobNumber(jobNumber)} disabled={!jobNumber.trim()}>Load Tracts</Btn>
      </div>
      {errorMessage && <p className="helper-text" style={{ color: 'var(--danger, #b3261e)' }}>{errorMessage}</p>}
      {lastResult && (
        <p className="helper-text top-gap-small">
          Marked {lastResult.marked.length} tract{lastResult.marked.length === 1 ? '' : 's'}.
          {lastResult.failures.length > 0 && (
            <>
              {' '}{lastResult.failures.length} could not be marked: {lastResult.failures.map((f) => `Case ${f.caseId} (${f.error})`).join('; ')}
            </>
          )}
        </p>
      )}

      {!searchedJobNumber ? (
        <EmptyState title="Enter a job number to load its tracts." description="Every Pipeline tract on that job appears below, ready for a bulk milestone mark." />
      ) : tracts.length === 0 ? (
        <EmptyState title={`No Pipeline tracts found for job ${searchedJobNumber}.`} description="Check the job number, or the tracts may have already left Pipeline status." />
      ) : (
        <div className="table-wrap top-gap-small">
          <table className="ui-table compact-table">
            <thead>
              <tr>
                <th>Tract</th>
                <th>Case Name</th>
                {PRE_FILING_MILESTONE_ORDER.map((milestone) => (
                  <th key={milestone}>{preFilingMilestoneLabel(milestone)}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {tracts.map((record) => (
                <tr key={record.id}>
                  <td>{record.tract || '—'}</td>
                  <td>{record.caseName}</td>
                  {PRE_FILING_MILESTONE_ORDER.map((milestone) => {
                    const marked = isMarked(record.id, milestone)
                    const eligible = canMark(record.id, milestone)
                    const markedRecord = milestonesByCase.get(record.id)?.get(milestone)
                    return (
                      <td key={milestone}>
                        {marked ? (
                          <span className="pill pill-success" title={markedRecord?.occurredDate || undefined}>
                            {markedRecord?.occurredDate || 'Marked'}
                          </span>
                        ) : (
                          <input
                            type="checkbox"
                            disabled={!eligible}
                            title={!eligible ? 'Prerequisite milestone not yet marked' : undefined}
                            checked={selected[milestone].has(record.id)}
                            onChange={() => toggleSelected(milestone, record.id)}
                          />
                        )}
                      </td>
                    )
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {PRE_FILING_MILESTONE_ORDER.map((milestone) => {
        const count = selectedCount(milestone)
        if (count === 0) return null
        return (
          <div key={milestone} className="review-notes-form top-gap-small">
            {activeColumn !== milestone ? (
              <Btn size="sm" onClick={() => setActiveColumn(milestone)}>
                Mark {count} tract{count === 1 ? '' : 's'} as {preFilingMilestoneLabel(milestone)}…
              </Btn>
            ) : (
              <div className="form-section-grid">
                <label>
                  <span>Occurred date (applies to all {count} selected)</span>
                  <input type="date" value={occurredDate} onChange={(event) => setOccurredDate(event.currentTarget.value)} />
                </label>
                <label className="full-span">
                  <span>Note (optional)</span>
                  <textarea rows={2} value={note} onChange={(event) => setNote(event.currentTarget.value)} />
                </label>
                <div className="button-row compact-actions full-span">
                  <Btn size="sm" disabled={busy} onClick={() => void markSelected(milestone)}>
                    Confirm: Mark {count} Tract{count === 1 ? '' : 's'}
                  </Btn>
                  <Btn size="sm" variant="ghost" onClick={() => setActiveColumn(null)}>Cancel</Btn>
                </div>
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}
