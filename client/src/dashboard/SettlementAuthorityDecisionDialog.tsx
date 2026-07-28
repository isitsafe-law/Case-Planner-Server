import { useState } from 'react'
import { ModalShell } from '../App'
import type { SettlementAuthorityRequestRecord } from './types'

// The one real, actionable decision in the Approvals tab (see ApprovalsTab.tsx's doc comment for
// the full Milestone 5 scope note). Structurally similar to RecordDecisionDialog (a required notes
// field driving a single submit) with PipelineHandoffDialog's "an extra field only sometimes
// applies" wrinkle - here, the optional Granted amount override only applies to the Approve action.
//
// Judgment call: the action is fixed for the lifetime of one dialog instance rather than
// switchable inside it. Each of the three action buttons in SettlementAuthoritySection's actions
// column opens its own instance of this dialog pre-set to that action - a Chief Counsel who clicked
// "Deny" sees a dialog titled/labeled for denial throughout, not a dialog that could be silently
// re-aimed at a different outcome before submit.
export type SettlementAuthorityDecisionAction = 'Approved' | 'Denied' | 'InfoRequested'

const ACTION_COPY: Record<SettlementAuthorityDecisionAction, { title: string; submitLabel: string; notesLabel: string; notesPlaceholder: string }> = {
  Approved: {
    title: 'Approve / Grant Settlement Authority',
    submitLabel: 'Approve',
    notesLabel: 'Comment',
    notesPlaceholder: 'Basis for the approved amount',
  },
  Denied: {
    title: 'Deny Settlement Authority Request',
    submitLabel: 'Deny',
    notesLabel: 'Comment',
    notesPlaceholder: 'Reason for denial',
  },
  InfoRequested: {
    title: 'Request More Information',
    submitLabel: 'Send Request',
    notesLabel: 'Comment',
    notesPlaceholder: 'What additional information is needed',
  },
}

export function SettlementAuthorityDecisionDialog({
  request,
  caseLabel,
  action,
  onClose,
  onSubmit,
}: {
  request: SettlementAuthorityRequestRecord
  caseLabel: string
  action: SettlementAuthorityDecisionAction
  onClose: () => void
  onSubmit: (payload: { action: SettlementAuthorityDecisionAction; comment: string; grantedAmount?: number }) => Promise<void>
}) {
  const [comment, setComment] = useState('')
  // Defaults to the request's own requestedAmount, matching the server's own default (see
  // DecideSettlementAuthorityRequestAsync's doc comment in CasePlannerRepository.cs: GrantedAmount
  // falls back to RequestedAmount whenever the deciding Chief Counsel doesn't override it).
  const [grantedAmount, setGrantedAmount] = useState<string>(String(request.requestedAmount))
  const [busy, setBusy] = useState(false)
  const copy = ACTION_COPY[action]
  // Client-side mirror of the server's own "blank comment throws" rule (see
  // DecideSettlementAuthorityRequestAsync), so an incomplete request never round-trips needlessly.
  const commentRequired = comment.trim() === ''

  return (
    <ModalShell title={`${copy.title}: ${caseLabel}`} onClose={onClose}>
      <form
        className="stacked-form"
        onSubmit={async (e) => {
          e.preventDefault()
          if (commentRequired) return
          setBusy(true)
          try {
            const parsedGranted = action === 'Approved' && grantedAmount.trim() !== '' ? Number(grantedAmount) : undefined
            await onSubmit({
              action,
              comment: comment.trim(),
              grantedAmount: parsedGranted !== undefined && Number.isFinite(parsedGranted) ? parsedGranted : undefined,
            })
          } finally {
            setBusy(false)
          }
        }}
      >
        <p className="field-change-summary">
          Requested amount: ${request.requestedAmount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
        </p>
        {action === 'Approved' && (
          <label>
            Granted amount (optional - defaults to the requested amount)
            <input
              type="number"
              step="0.01"
              min="0"
              value={grantedAmount}
              onChange={(e) => setGrantedAmount(e.currentTarget.value)}
              placeholder={String(request.requestedAmount)}
            />
          </label>
        )}
        <label>
          {copy.notesLabel} (required)
          <textarea
            value={comment}
            onChange={(e) => setComment(e.currentTarget.value)}
            rows={3}
            placeholder={copy.notesPlaceholder}
            required
          />
        </label>
        <div className="button-row">
          <button className="primary" type="submit" disabled={busy || commentRequired}>{copy.submitLabel}</button>
          <button type="button" onClick={onClose}>Cancel</button>
        </div>
      </form>
    </ModalShell>
  )
}
