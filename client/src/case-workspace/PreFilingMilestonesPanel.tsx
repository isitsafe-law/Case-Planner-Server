import { useEffect, useState } from 'react'
import { api } from '../App'
import { Btn } from '../ui/Btn'
import { formatDate } from '../ui/format'
import {
  PRE_FILING_MILESTONE_ORDER,
  preFilingMilestoneLabel,
  type PreFilingMilestone,
  type PreFilingMilestoneRecord,
} from '../dashboard/types'

// The client UI for the server-enforced pre-filing sign-off gate (see PreFilingMilestoneGate /
// EnsureFilingReady on the server) - the gate itself, the 4-milestone order, and the override
// mechanism (CaseRecord.FilingGateOverrideReason) all already existed and were already tested
// server-side; this panel is the previously-missing way to actually mark/unmark a milestone or
// invoke the override from the case workspace. Lives in the case editor (see App.tsx's
// placement-decision comment at its call site) rather than the read-only Manager Dashboard's
// FilingStatusSection, which deliberately stays read-only.
//
// Manager Dashboard sign-off consolidation, item 3: the Director signature gate is a soft
// forcing-prompt now, not a hard block restricted to managers - EnsureFilingReady no longer checks
// actorRole at all, so any actor can supply the override reason. autoOpenOverride lets App.tsx's
// saveCase pre-expand this control the moment a save is blocked on it, so the "continue anyway"
// path is one click (type a reason, save) rather than requiring the user to first discover and
// click a separate toggle.

// Client-side mirror of the server's "mark milestone N requires N-1 already marked" rule - purely
// a clearer UX than letting the click round-trip and fail, the server enforces this regardless.
// Returns the label of the missing prerequisite, or null when this milestone is clear to mark.
export function missingPrerequisiteLabel(milestones: PreFilingMilestoneRecord[], milestone: PreFilingMilestone): string | null {
  const index = PRE_FILING_MILESTONE_ORDER.indexOf(milestone)
  if (index <= 0) return null
  const previous = PRE_FILING_MILESTONE_ORDER[index - 1]
  const previousRecord = milestones.find((record) => record.milestone === previous)
  if (previousRecord?.isMarked) return null
  return preFilingMilestoneLabel(previous)
}

// Mirror of the server's "unmark N requires no later milestone still marked" rule. Returns the
// label of the first later milestone still marked, or null when this milestone is clear to unmark.
export function laterMarkedMilestoneLabel(milestones: PreFilingMilestoneRecord[], milestone: PreFilingMilestone): string | null {
  const index = PRE_FILING_MILESTONE_ORDER.indexOf(milestone)
  for (let i = index + 1; i < PRE_FILING_MILESTONE_ORDER.length; i++) {
    const later = PRE_FILING_MILESTONE_ORDER[i]
    const laterRecord = milestones.find((record) => record.milestone === later)
    if (laterRecord?.isMarked) return preFilingMilestoneLabel(later)
  }
  return null
}

// Same UTC-slice convention used elsewhere in this app for a "today" default (see the CSV export
// filenames in FilingStatusSection.tsx) - the occurred-date input is explicitly editable/backdatable
// per spec, this is only the starting value.
function todayIsoDate(): string {
  return new Date().toISOString().slice(0, 10)
}

type MarkDraft = { occurredDate: string; note: string }

export function PreFilingMilestonesPanel({
  caseId,
  filingGateOverrideReason,
  onOverrideReasonChange,
  onMutated,
  autoOpenOverride,
  visibleMilestones,
  showOverride = true,
}: {
  caseId: number
  filingGateOverrideReason?: string
  onOverrideReasonChange: (value: string | undefined) => void
  onMutated: () => Promise<void>
  autoOpenOverride?: boolean
  visibleMilestones?: PreFilingMilestone[]
  showOverride?: boolean
}) {
  const [milestones, setMilestones] = useState<PreFilingMilestoneRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [errorMessage, setErrorMessage] = useState('')
  const [markDrafts, setMarkDrafts] = useState<Record<string, MarkDraft>>({})
  const [noteOpenFor, setNoteOpenFor] = useState<string | null>(null)
  const [unmarkOpenFor, setUnmarkOpenFor] = useState<string | null>(null)
  const [unmarkReason, setUnmarkReason] = useState('')
  const [busyMilestone, setBusyMilestone] = useState<string | null>(null)
  const [overrideOpen, setOverrideOpen] = useState(Boolean(filingGateOverrideReason))

  // A blocked save (App.tsx's saveCase) sets autoOpenOverride so the "continue anyway" path is
  // immediately visible rather than requiring the user to first find and click a separate toggle -
  // the soft-forcing-prompt behavior Manager Dashboard sign-off consolidation item 3 calls for.
  useEffect(() => {
    if (autoOpenOverride) setOverrideOpen(true)
  }, [autoOpenOverride])

  useEffect(() => {
    let cancelled = false
    async function load() {
      setLoading(true)
      setErrorMessage('')
      try {
        const data = await api<PreFilingMilestoneRecord[]>(`/api/cases/${caseId}/prefiling-milestones`)
        if (!cancelled) setMilestones(data)
      } catch (error) {
        if (!cancelled) setErrorMessage(error instanceof Error ? error.message : 'Unable to load pre-filing milestones.')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }
    // Reset per-case UI state (an open Unmark form, a half-typed override reason toggle) so
    // switching cases while this panel is mounted never leaks one case's in-progress action onto
    // another's milestones.
    setUnmarkOpenFor(null)
    setUnmarkReason('')
    setMarkDrafts({})
    setNoteOpenFor(null)
    if (caseId) void load()
    return () => {
      cancelled = true
    }
  }, [caseId])

  async function refetch() {
    try {
      setMilestones(await api<PreFilingMilestoneRecord[]>(`/api/cases/${caseId}/prefiling-milestones`))
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to reload pre-filing milestones.')
    }
  }

  async function mark(milestone: PreFilingMilestone) {
    const draft = markDrafts[milestone] ?? { occurredDate: todayIsoDate(), note: '' }
    setBusyMilestone(milestone)
    try {
      setErrorMessage('')
      if (milestone === 'DirectorSignatureReceived') {
        await api(`/api/cases/${caseId}/prefiling-review`, {
          method: 'POST',
          body: JSON.stringify({
            action: 'DirectorSignature',
            occurredAt: draft.occurredDate,
            note: draft.note.trim() || undefined,
          }),
        })
      } else {
        await api(`/api/cases/${caseId}/prefiling-milestones/${milestone}/mark`, {
          method: 'POST',
          body: JSON.stringify({ occurredDate: draft.occurredDate, note: draft.note.trim() || undefined }),
        })
      }
      setMarkDrafts((current) => {
        const next = { ...current }
        delete next[milestone]
        return next
      })
      setNoteOpenFor(null)
      await refetch()
      await onMutated()
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : `Unable to mark ${preFilingMilestoneLabel(milestone)}.`)
    } finally {
      setBusyMilestone(null)
    }
  }

  async function unmark(milestone: PreFilingMilestone) {
    if (!unmarkReason.trim()) return
    setBusyMilestone(milestone)
    try {
      setErrorMessage('')
      await api(`/api/cases/${caseId}/prefiling-milestones/${milestone}/unmark`, {
        method: 'POST',
        body: JSON.stringify({ reason: unmarkReason.trim() }),
      })
      setUnmarkOpenFor(null)
      setUnmarkReason('')
      await refetch()
      await onMutated()
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : `Unable to unmark ${preFilingMilestoneLabel(milestone)}.`)
    } finally {
      setBusyMilestone(null)
    }
  }

  return (
    <div className="prefiling-milestones-panel">
      <p className="helper-text">
        Record when each pre-filing step actually happened. These are facts about what already occurred, not requests sent to anyone -
        marking a step here doesn't notify the Chief Counsel or the Director.
      </p>
      {errorMessage && <p className="helper-text" style={{ color: 'var(--danger, #b3261e)' }}>{errorMessage}</p>}
      {loading ? (
        <p className="helper-text">Loading pre-filing milestones…</p>
      ) : (
        <div className="prefiling-milestone-list">
          {(() => {
            const coreMilestones = visibleMilestones ?? PRE_FILING_MILESTONE_ORDER
            const pleadingsSent = milestones.find((item) => item.milestone === 'PleadingsPackageSent')?.isMarked
            const chiefSigned = milestones.find((item) => item.milestone === 'ChiefCounselSignaturesReceived')?.isMarked
            const derivedStatus = chiefSigned ? 'Review complete — proceeding to filing' : pleadingsSent ? 'Awaiting chief counsel review' : 'Pleadings preparation'
            return <>
              <p className="prefiling-derived-status"><strong>{derivedStatus}</strong></p>
              {coreMilestones.map((milestone) => {
            const record = milestones.find((item) => item.milestone === milestone)
            const isMarked = Boolean(record?.isMarked)
            const isBusy = busyMilestone === milestone
            const draft = markDrafts[milestone] ?? { occurredDate: todayIsoDate(), note: '' }
            const missingPrereq = missingPrerequisiteLabel(milestones, milestone)
            const laterMarked = laterMarkedMilestoneLabel(milestones, milestone)

            return (
              <div key={milestone} className="prefiling-milestone-row">
                <div className="prefiling-milestone-row-header">
                  <strong>{preFilingMilestoneLabel(milestone)}</strong>
                  <span className={`pill ${isMarked ? 'pill-success' : 'pill-neutral'}`}>{isMarked ? 'Marked' : 'Not marked'}</span>
                </div>

                {isMarked ? (
                  <div className="prefiling-milestone-marked-detail">
                    <p className="subtle-text">
                      {formatDate(record?.occurredDate)}
                      {record?.markedByDisplay ? ` · ${record.markedByDisplay}` : ''}
                      {record?.markedByRole ? ` (${record.markedByRole})` : ''}
                    </p>
                    {record?.note && (
                      <details className="prefiling-note-detail">
                        <summary>Note available</summary>
                        <p className="helper-text">{record.note}</p>
                      </details>
                    )}

                    {unmarkOpenFor === milestone ? (
                      <div className="prefiling-unmark-form top-gap-small">
                        <label>
                          <span>Reason for unmarking (required)</span>
                          <textarea
                            rows={2}
                            value={unmarkReason}
                            onChange={(event) => setUnmarkReason(event.currentTarget.value)}
                            placeholder="Why is this being reversed?"
                          />
                        </label>
                        {laterMarked && (
                          <p className="helper-text">{laterMarked} is still marked - unmark that first.</p>
                        )}
                        <div className="button-row compact-actions top-gap-small">
                          <Btn
                            size="sm"
                            variant="danger"
                            disabled={unmarkReason.trim() === '' || Boolean(laterMarked) || isBusy}
                            onClick={() => void unmark(milestone)}
                          >
                            Confirm Unmark
                          </Btn>
                          <Btn size="sm" variant="ghost" onClick={() => { setUnmarkOpenFor(null); setUnmarkReason('') }}>
                            Cancel
                          </Btn>
                        </div>
                      </div>
                    ) : (
                      <button
                        type="button"
                        className="compact-action-button top-gap-small"
                        onClick={() => { setUnmarkOpenFor(milestone); setUnmarkReason('') }}
                      >
                        Unmark…
                      </button>
                    )}
                  </div>
                ) : (
                  <div className="prefiling-mark-form">
                    <div className="form-section-grid prefiling-mark-compact">
                      <label>
                        <span>Occurred date</span>
                        <input
                          type="date"
                          value={draft.occurredDate}
                          onChange={(event) => setMarkDrafts((current) => ({ ...current, [milestone]: { ...draft, occurredDate: event.currentTarget.value } }))}
                        />
                      </label>
                      {noteOpenFor === milestone && <label className="full-span"><span>Note (optional)</span><textarea rows={2} value={draft.note} onChange={(event) => setMarkDrafts((current) => ({ ...current, [milestone]: { ...draft, note: event.currentTarget.value } }))} placeholder={milestone === 'PleadingsPackageSent' ? 'What was included?' : 'Optional note'} /></label>}
                    </div>
                    {missingPrereq && (
                      <p className="helper-text">{missingPrereq} must be marked first.</p>
                    )}
                    <div className="button-row compact-actions top-gap-small">
                      <Btn size="sm" aria-label="Mark" disabled={Boolean(missingPrereq) || isBusy} onClick={() => void mark(milestone)}>
                        Mark complete
                      </Btn>
                      <Btn size="sm" variant="ghost" onClick={() => setNoteOpenFor(noteOpenFor === milestone ? null : milestone)}>{noteOpenFor === milestone ? 'Hide note' : 'Add note'}</Btn>
                    </div>
                  </div>
                )}
              </div>
            )
              })}
            </>
          })()}
        </div>
      )}

      {showOverride && <div className="prefiling-override top-gap-small">
        {!overrideOpen ? (
          <button type="button" className="link-button" onClick={() => setOverrideOpen(true)}>
            Continue Without Marking…
          </button>
        ) : (
          <div>
            <label>
              <span>Reason for continuing without the Director Signature Received milestone (required)</span>
              <textarea
                rows={2}
                value={filingGateOverrideReason || ''}
                onChange={(event) => onOverrideReasonChange(event.currentTarget.value)}
                placeholder="Why is this case leaving Pipeline without the Director signature milestone?"
              />
            </label>
            <button
              type="button"
              className="link-button top-gap-small"
              onClick={() => { setOverrideOpen(false); onOverrideReasonChange(undefined) }}
            >
              Cancel
            </button>
          </div>
        )}
      </div>}
    </div>
  )
}
