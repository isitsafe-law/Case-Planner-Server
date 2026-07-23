import type { FormEvent, KeyboardEvent } from 'react'
import { useEffect, useRef, useState } from 'react'

// Close Case dialog (multi-user rollout Phase 5, reporting data-capture track) - replaces the
// window.prompt() that used to collect only Closed Date when changeStatus() transitions a case to
// Closed. Modeled after ConfirmDialog's overlay/panel shell and focus/Escape/outside-click
// conventions, but this needs real form fields (date + two required inputs), not just yes/no, so
// it's its own small component rather than a reuse of ConfirmDialog itself.

export const dispositionTypeOptions = ['Jury Trial', 'Settlement', 'Mediation'] as const

export type CloseCaseDetails = {
  closedDate: string
  dispositionType: string
  finalJudgmentAmount: number
  // Test-build feedback batch, items 2 & 3: a plain manually-entered fact, not auto-validated
  // against the statutory attorney's-fee-shift threshold (Ark. Code Ann. Sec. 27-67-317(b), which
  // only ever applies to a jury verdict, never a settlement) - that verification is already
  // prompted separately by the "Post-Trial - Core" checklist template. Unlike
  // dispositionType/finalJudgmentAmount above, neither of these is required to close the case -
  // attorneyFeesAmount is only ever meaningful when attorneyFeesAwarded is true, and even then
  // isn't force-required (see canSubmit below).
  attorneyFeesAwarded: boolean
  attorneyFeesAmount: number | null
}

export function CloseCaseDialog({
  initialClosedDate,
  initialDispositionType,
  initialFinalJudgmentAmount,
  initialAttorneyFeesAwarded,
  initialAttorneyFeesAmount,
  onSubmit,
  onCancel,
}: {
  initialClosedDate: string
  initialDispositionType?: string | null
  initialFinalJudgmentAmount?: number | null
  initialAttorneyFeesAwarded?: boolean | null
  initialAttorneyFeesAmount?: number | null
  onSubmit: (details: CloseCaseDetails) => void
  onCancel: () => void
}) {
  const [closedDate, setClosedDate] = useState(initialClosedDate)
  const [dispositionType, setDispositionType] = useState(initialDispositionType || '')
  const [amountText, setAmountText] = useState(initialFinalJudgmentAmount != null ? String(initialFinalJudgmentAmount) : '')
  const [attorneyFeesAwarded, setAttorneyFeesAwarded] = useState(Boolean(initialAttorneyFeesAwarded))
  const [feesAmountText, setFeesAmountText] = useState(initialAttorneyFeesAmount != null ? String(initialAttorneyFeesAmount) : '')
  const firstFieldRef = useRef<HTMLInputElement | null>(null)
  const previouslyFocused = useRef<HTMLElement | null>(null)

  // Focus the first field on open; restore focus to whatever invoked the dialog on close/unmount,
  // matching ConfirmDialog's focus-return behavior.
  useEffect(() => {
    previouslyFocused.current = document.activeElement as HTMLElement | null
    firstFieldRef.current?.focus()
    return () => {
      previouslyFocused.current?.focus?.()
    }
  }, [])

  function handleKeyDown(event: KeyboardEvent<HTMLElement>) {
    if (event.key === 'Escape') {
      event.preventDefault()
      event.stopPropagation()
      onCancel()
    }
  }

  const amountValue = Number(amountText)
  const feesAmountValue = Number(feesAmountText)
  const canSubmit = Boolean(closedDate) &&
    (dispositionTypeOptions as readonly string[]).includes(dispositionType) &&
    amountText.trim() !== '' && Number.isFinite(amountValue)

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!canSubmit) return
    onSubmit({
      closedDate,
      dispositionType,
      finalJudgmentAmount: amountValue,
      attorneyFeesAwarded,
      attorneyFeesAmount: attorneyFeesAwarded && feesAmountText.trim() !== '' && Number.isFinite(feesAmountValue) ? feesAmountValue : null,
    })
  }

  return (
    <div className="ui-command-overlay" role="presentation" onClick={onCancel}>
      <section
        className="ui-command-palette ui-confirm-dialog ui-close-case-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="close-case-dialog-title"
        onClick={(event) => event.stopPropagation()}
        onKeyDown={handleKeyDown}
      >
        <h2 id="close-case-dialog-title" className="ui-confirm-title">Close case</h2>
        <p className="ui-confirm-message">Record how this case was resolved before marking it Closed.</p>
        <form onSubmit={handleSubmit} className="ui-close-case-form">
          <label>
            <span>Closed Date</span>
            <input
              ref={firstFieldRef}
              type="date"
              value={closedDate}
              onChange={(event) => setClosedDate(event.target.value)}
              required
            />
          </label>
          <label>
            <span>Disposition Type</span>
            <select value={dispositionType} onChange={(event) => setDispositionType(event.target.value)} required>
              <option value="">Select disposition type</option>
              {dispositionTypeOptions.map((option) => (
                <option key={option} value={option}>{option}</option>
              ))}
            </select>
          </label>
          <label>
            <span>Final Judgment / Settlement Amount</span>
            <input
              type="number"
              step="0.01"
              min="0"
              value={amountText}
              onChange={(event) => setAmountText(event.target.value)}
              placeholder="0.00"
              required
            />
          </label>
          <label className="toggle-inline">
            <span>Attorney's Fees Awarded?</span>
            <input
              type="checkbox"
              checked={attorneyFeesAwarded}
              onChange={(event) => setAttorneyFeesAwarded(event.target.checked)}
            />
          </label>
          {attorneyFeesAwarded && (
            <label>
              <span>Attorney's Fees Amount</span>
              <input
                type="number"
                step="0.01"
                min="0"
                value={feesAmountText}
                onChange={(event) => setFeesAmountText(event.target.value)}
                placeholder="0.00"
              />
            </label>
          )}
          <div className="ui-confirm-actions">
            <button type="button" className="ui-btn ui-btn-secondary" onClick={onCancel}>Cancel</button>
            <button type="submit" className="ui-btn ui-btn-primary" disabled={!canSubmit}>Mark Closed</button>
          </div>
        </form>
      </section>
    </div>
  )
}
