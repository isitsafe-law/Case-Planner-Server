import { useEffect, useState } from 'react'
import { api } from '../App'
import { Btn } from '../ui/Btn'
import { formatDate } from '../ui/format'
import type { ReviewNoteRecord } from '../dashboard/types'

// Pre-filing sign-off/Settlement Authority final implementation, item 2: an unstructured,
// append-only review-note log - deliberately separate in shape and display from
// PreFilingMilestonesPanel.tsx's ordered milestone grid (see ReviewNoteRecord's doc comment in
// DomainModels.cs). Rendered as its own log alongside the milestone grid on the case workspace, not
// interleaved into it - merging the two would visually suggest the milestones depend on a review,
// which they don't. No fixed order, no required participant: any case with write access can add a
// note at any point, about any milestone or none at all.

// A few common values the client suggests, matching the vocabulary the spec itself uses - but the
// server stores and returns whatever string comes through, so "Other" reveals a free-text field
// rather than constraining input to this list. IsReturnedForRevision below matches this exact
// string, case-insensitively - keep them in sync.
const DECISION_SUGGESTIONS = ['Looks good', 'Sent back for revision', 'Other'] as const

// Client-side mirror of the server's stall-detection matching rule - exported so both this log
// display and the shared stall detector (preFilingStallDetection.ts, used by NeedsAttentionTab and
// ByAttorneyTab) agree on what counts as a "sent back" note without duplicating the literal string.
export function isReturnedForRevisionDecision(decision: string): boolean {
  return decision.trim().toLowerCase() === 'sent back for revision'
}

function todayIsoDate(): string {
  return new Date().toISOString().slice(0, 10)
}

export function ReviewNotesLog({ caseId, onAdded }: { caseId: number; onAdded?: () => void }) {
  const [notes, setNotes] = useState<ReviewNoteRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [errorMessage, setErrorMessage] = useState('')
  const [formOpen, setFormOpen] = useState(false)
  const [reviewerName, setReviewerName] = useState('')
  const [reviewerRole, setReviewerRole] = useState('')
  const [decisionChoice, setDecisionChoice] = useState<string>(DECISION_SUGGESTIONS[0])
  const [customDecision, setCustomDecision] = useState('')
  const [comment, setComment] = useState('')
  const [occurredDate, setOccurredDate] = useState(todayIsoDate())
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let cancelled = false
    async function load() {
      setLoading(true)
      setErrorMessage('')
      try {
        const data = await api<ReviewNoteRecord[]>(`/api/cases/${caseId}/review-notes`)
        if (!cancelled) setNotes(data)
      } catch (error) {
        if (!cancelled) setErrorMessage(error instanceof Error ? error.message : 'Unable to load review notes.')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }
    if (caseId) void load()
    return () => {
      cancelled = true
    }
  }, [caseId])

  function resetForm() {
    setReviewerName('')
    setReviewerRole('')
    setDecisionChoice(DECISION_SUGGESTIONS[0])
    setCustomDecision('')
    setComment('')
    setOccurredDate(todayIsoDate())
  }

  async function addNote() {
    const decision = decisionChoice === 'Other' ? customDecision.trim() : decisionChoice
    if (!decision) return
    setBusy(true)
    try {
      setErrorMessage('')
      await api(`/api/cases/${caseId}/review-notes`, {
        method: 'POST',
        body: JSON.stringify({
          reviewerName: reviewerName.trim() || undefined,
          reviewerRole: reviewerRole.trim() || undefined,
          decision,
          comment: comment.trim() || undefined,
          occurredDate,
        }),
      })
      setNotes(await api<ReviewNoteRecord[]>(`/api/cases/${caseId}/review-notes`))
      resetForm()
      setFormOpen(false)
      await onAdded?.()
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to add the review note.')
    } finally {
      setBusy(false)
    }
  }

  const decisionReady = decisionChoice !== 'Other' || customDecision.trim() !== ''

  return (
    <div className="review-notes-log">
      <p className="helper-text">
        A running log of reviews on this file - no fixed order, no required reviewer. Adding a note here doesn't block or require
        anything else; it's a record of what someone noticed, when.
      </p>
      {errorMessage && <p className="helper-text" style={{ color: 'var(--danger, #b3261e)' }}>{errorMessage}</p>}
      {loading ? (
        <p className="helper-text">Loading review notes…</p>
      ) : notes.length === 0 ? (
        <p className="helper-text">No review notes yet.</p>
      ) : (
        <ul className="review-notes-list plain-list">
          {notes.map((note) => (
            <li key={note.id} className="review-notes-item top-gap-small">
              <div className="review-notes-item-header" style={{ display: 'flex', justifyContent: 'space-between', gap: '0.5rem' }}>
                <strong>{note.decision}</strong>
                <span className="subtle-text">{formatDate(note.occurredDate)}</span>
              </div>
              <p className="subtle-text">
                {note.reviewerName || 'Unspecified reviewer'}
                {note.reviewerRole ? ` (${note.reviewerRole})` : ''}
              </p>
              {note.comment && <p className="helper-text">{note.comment}</p>}
            </li>
          ))}
        </ul>
      )}

      {!formOpen ? (
        <button type="button" className="link-button top-gap-small" onClick={() => setFormOpen(true)}>
          Add Review Note…
        </button>
      ) : (
        <div className="review-notes-form top-gap-small">
          <div className="form-section-grid">
            <label>
              <span>Reviewer name (optional)</span>
              <input value={reviewerName} onChange={(event) => setReviewerName(event.currentTarget.value)} placeholder="Who reviewed it" />
            </label>
            <label>
              <span>Reviewer role (optional)</span>
              <input value={reviewerRole} onChange={(event) => setReviewerRole(event.currentTarget.value)} placeholder="e.g. Deputy Chief Counsel" />
            </label>
            <label>
              <span>Decision</span>
              <select value={decisionChoice} onChange={(event) => setDecisionChoice(event.currentTarget.value)}>
                {DECISION_SUGGESTIONS.map((option) => (
                  <option key={option} value={option}>{option}</option>
                ))}
              </select>
            </label>
            {decisionChoice === 'Other' && (
              <label>
                <span>Decision (custom)</span>
                <input value={customDecision} onChange={(event) => setCustomDecision(event.currentTarget.value)} placeholder="Type a decision" />
              </label>
            )}
            <label>
              <span>Date</span>
              <input type="date" value={occurredDate} onChange={(event) => setOccurredDate(event.currentTarget.value)} />
            </label>
            <label className="full-span">
              <span>Comment (optional)</span>
              <textarea rows={2} value={comment} onChange={(event) => setComment(event.currentTarget.value)} placeholder="What was noticed" />
            </label>
          </div>
          <div className="button-row compact-actions top-gap-small">
            <Btn size="sm" disabled={busy || !decisionReady} onClick={() => void addNote()}>Add Note</Btn>
            <Btn size="sm" variant="ghost" onClick={() => { resetForm(); setFormOpen(false) }}>Cancel</Btn>
          </div>
        </div>
      )}
    </div>
  )
}
