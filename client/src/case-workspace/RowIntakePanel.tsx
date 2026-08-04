import { useEffect, useState } from 'react'
import { api } from '../App'
import { Btn } from '../ui/Btn'
import { formatDate, formatDateTime } from '../ui/format'
import {
  ROW_INTAKE_STATUSES,
  ROW_INTAKE_TERMINAL_STATUSES,
  type PrefilingReviewEventRecord,
  type RowIntakeStatus,
} from '../dashboard/types'

// Client UI for RecordTitleReviewRoundAsync (see server/CasePlanner.Web.Server/Services/
// CasePlannerRepository.cs) - a separate action space from PreFilingMilestonesPanel's internal
// holder-chain review (Legal Assistant -> Attorney -> Deputy Chief Counsel -> Chief Counsel).
// RowIntakeStatus tracks an earlier, orthogonal stage: where a tract sits relative to ROW/the
// title attorney, before it's even assigned to an attorney for that internal review. Reviewer
// name is deliberately typed fresh every round rather than defaulted from the acting user - the
// person recording a round in the system may not be the title attorney who actually reviewed it.

function todayIsoDate(): string {
  return new Date().toISOString().slice(0, 10)
}

function statusPillClass(status: string | null | undefined): string {
  if (!status) return 'pill-neutral'
  if ((ROW_INTAKE_TERMINAL_STATUSES as string[]).includes(status)) return 'pill-neutral'
  if (status === 'Returned to ROW') return 'pill-warning'
  return 'pill-success'
}

export function RowIntakePanel({
  caseId,
  rowIntakeStatus,
  onMutated,
}: {
  caseId: number
  rowIntakeStatus?: string | null
  onMutated: () => Promise<void>
}) {
  const [rounds, setRounds] = useState<PrefilingReviewEventRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [errorMessage, setErrorMessage] = useState('')
  const [formOpen, setFormOpen] = useState(false)
  const [outcome, setOutcome] = useState<RowIntakeStatus>('In Title Review')
  const [reviewerDisplay, setReviewerDisplay] = useState('')
  const [note, setNote] = useState('')
  const [occurredAt, setOccurredAt] = useState(todayIsoDate())
  const [busy, setBusy] = useState(false)

  async function refetch() {
    const events = await api<PrefilingReviewEventRecord[]>(`/api/cases/${caseId}/prefiling-review/events`)
    setRounds(events.filter((event) => event.eventType === 'TitleReview'))
  }

  useEffect(() => {
    let cancelled = false
    async function load() {
      setLoading(true)
      setErrorMessage('')
      try {
        const events = await api<PrefilingReviewEventRecord[]>(`/api/cases/${caseId}/prefiling-review/events`)
        if (!cancelled) setRounds(events.filter((event) => event.eventType === 'TitleReview'))
      } catch (error) {
        if (!cancelled) setErrorMessage(error instanceof Error ? error.message : 'Unable to load title-review rounds.')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }
    setFormOpen(false)
    if (caseId) void load()
    return () => {
      cancelled = true
    }
  }, [caseId])

  function resetForm() {
    setOutcome('In Title Review')
    setReviewerDisplay('')
    setNote('')
    setOccurredAt(todayIsoDate())
  }

  async function recordRound() {
    if (!reviewerDisplay.trim()) return
    setBusy(true)
    try {
      setErrorMessage('')
      await api(`/api/cases/${caseId}/prefiling-review/title-review`, {
        method: 'POST',
        body: JSON.stringify({
          outcome,
          reviewerDisplay: reviewerDisplay.trim(),
          note: note.trim() || undefined,
          occurredAt,
        }),
      })
      await refetch()
      resetForm()
      setFormOpen(false)
      await onMutated()
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to record the title-review round.')
    } finally {
      setBusy(false)
    }
  }

  // rounds is fetched fresh by this panel and is ordered newest-first (server returns
  // ORDER BY id DESC), so its first entry reflects a just-recorded round immediately - unlike the
  // rowIntakeStatus prop, which reflects whatever case snapshot the parent last loaded and may lag
  // behind until the parent's own refresh completes.
  const displayedStatus = rounds[0]?.outcome ?? rowIntakeStatus

  return (
    <div className="row-intake-panel">
      <div className="prefiling-milestone-row-header">
        <strong>ROW intake status</strong>
        <span className={`pill ${statusPillClass(displayedStatus)}`}>{displayedStatus || 'Not tracked through ROW intake'}</span>
      </div>
      <p className="helper-text">
        Tracks where this tract sits with ROW/the title attorney - a different axis from the current holder above. Recording a round
        updates this status and appends to the history below.
      </p>
      {errorMessage && <p className="helper-text" style={{ color: 'var(--danger, #b3261e)' }}>{errorMessage}</p>}

      {loading ? (
        <p className="helper-text">Loading title-review history…</p>
      ) : rounds.length === 0 ? (
        <p className="helper-text">No title-review rounds recorded yet.</p>
      ) : (
        <ul className="review-notes-list plain-list">
          {rounds.map((round) => (
            <li key={round.id} className="review-notes-item top-gap-small">
              <div className="review-notes-item-header" style={{ display: 'flex', justifyContent: 'space-between', gap: '0.5rem' }}>
                <strong>{round.outcome}</strong>
                <span className="subtle-text">{formatDate(round.occurredAt)}</span>
              </div>
              <p className="subtle-text">
                {round.reviewerDisplay || 'Unspecified reviewer'}
                {round.recordedByDisplay ? ` · recorded by ${round.recordedByDisplay} (${formatDateTime(round.recordedAt)})` : ''}
              </p>
              {round.note && <p className="helper-text">{round.note}</p>}
            </li>
          ))}
        </ul>
      )}

      {!formOpen ? (
        <button type="button" className="link-button top-gap-small" onClick={() => setFormOpen(true)}>
          Record Title-Review Round…
        </button>
      ) : (
        <div className="review-notes-form top-gap-small">
          <div className="form-section-grid">
            <label>
              <span>Outcome</span>
              <select value={outcome} onChange={(event) => setOutcome(event.currentTarget.value as RowIntakeStatus)}>
                {ROW_INTAKE_STATUSES.map((status) => (
                  <option key={status} value={status}>{status}</option>
                ))}
              </select>
            </label>
            <label>
              <span>Title reviewer (required)</span>
              <input
                value={reviewerDisplay}
                onChange={(event) => setReviewerDisplay(event.currentTarget.value)}
                placeholder="Who reviewed the title this round"
              />
            </label>
            <label>
              <span>Date</span>
              <input type="date" value={occurredAt} onChange={(event) => setOccurredAt(event.currentTarget.value)} />
            </label>
            <label className="full-span">
              <span>Note (optional)</span>
              <textarea rows={2} value={note} onChange={(event) => setNote(event.currentTarget.value)} placeholder="What happened this round" />
            </label>
          </div>
          <div className="button-row compact-actions top-gap-small">
            <Btn size="sm" disabled={busy || !reviewerDisplay.trim()} onClick={() => void recordRound()}>Record Round</Btn>
            <Btn size="sm" variant="ghost" onClick={() => { resetForm(); setFormOpen(false) }}>Cancel</Btn>
          </div>
        </div>
      )}
    </div>
  )
}
