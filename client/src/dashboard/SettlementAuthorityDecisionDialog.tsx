import { useState } from 'react'
import { ModalShell } from '../App'
import type { SettlementAuthorityRequestRecord } from './types'

// Records an outcome in the Approvals tab's Settlement Authority log (see ApprovalsTab.tsx's doc
// comment for the full Milestone 5 scope note; Manager Dashboard sign-off consolidation item 4 made
// this pure record-keeping, open to anyone with case-write access). Structurally similar to
// RecordDecisionDialog (a required notes field driving a single submit) with PipelineHandoffDialog's
// "an extra field only sometimes applies" wrinkle - the optional Granted amount/by/role/date fields
// only apply to the Approve action, since only that outcome is a "grant."
//
// Judgment call: the action is fixed for the lifetime of one dialog instance rather than
// switchable inside it. Each of the three action buttons in SettlementAuthoritySection's actions
// column opens its own instance of this dialog pre-set to that action - whoever clicked "Record
// Denial" sees a dialog titled/labeled for denial throughout, not a dialog that could be silently
// re-aimed at a different outcome before submit.
export type SettlementAuthorityDecisionAction = 'Approved' | 'Denied' | 'InfoRequested'

// Manager Dashboard sign-off consolidation, item 4: GrantedBy/GrantedByRole/GrantedDate are the
// real-world "who actually granted this and when" facts, distinct from the recorded-at/recorded-by
// info the server derives automatically from whoever is submitting this form - see
// SettlementAuthorityRequestRecord's doc comment (dashboard/types.ts) for why these can differ.
export type SettlementAuthorityDecisionPayload = {
  action: SettlementAuthorityDecisionAction
  comment: string
  grantedAmount?: number
  grantedBy?: string
  grantedByRole?: string
  grantedDate?: string
  documentReference?: string
}

const ACTION_COPY: Record<SettlementAuthorityDecisionAction, { title: string; submitLabel: string; notesLabel: string; notesPlaceholder: string }> = {
  Approved: {
    title: 'Record Settlement Authority Grant',
    submitLabel: 'Record Grant',
    notesLabel: 'Comment',
    notesPlaceholder: 'Basis for the granted amount',
  },
  Denied: {
    title: 'Record Settlement Authority Denial',
    submitLabel: 'Record Denial',
    notesLabel: 'Comment',
    notesPlaceholder: 'Reason for denial',
  },
  InfoRequested: {
    title: 'Record Info Requested',
    submitLabel: 'Record',
    notesLabel: 'Comment',
    notesPlaceholder: 'What additional information is needed',
  },
}

// Same UTC-slice convention used elsewhere in this app for a "today" default (see
// PreFilingMilestonesPanel.tsx's todayIsoDate) - the granted-date input is explicitly
// editable/backdatable per spec, this is only the starting value.
function todayIsoDate(): string {
  return new Date().toISOString().slice(0, 10)
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
  onSubmit: (payload: SettlementAuthorityDecisionPayload) => Promise<void>
}) {
  const [comment, setComment] = useState('')
  // Defaults to the request's own requestedAmount, matching the server's own default (see
  // DecideSettlementAuthorityRequestAsync's doc comment in CasePlannerRepository.cs: GrantedAmount
  // falls back to RequestedAmount whenever whoever records the grant doesn't override it).
  const [grantedAmount, setGrantedAmount] = useState<string>(String(request.requestedAmount))
  const [grantedBy, setGrantedBy] = useState('')
  const [grantedByRole, setGrantedByRole] = useState('')
  const [grantedDate, setGrantedDate] = useState(todayIsoDate())
  const [documentReference, setDocumentReference] = useState('')
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
              grantedBy: action === 'Approved' ? (grantedBy.trim() || undefined) : undefined,
              grantedByRole: action === 'Approved' ? (grantedByRole.trim() || undefined) : undefined,
              grantedDate: action === 'Approved' ? grantedDate : undefined,
              documentReference: documentReference.trim() || undefined,
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
          <>
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
            <div className="form-section-grid">
              <label>
                Granted by (optional)
                <input
                  type="text"
                  value={grantedBy}
                  onChange={(e) => setGrantedBy(e.currentTarget.value)}
                  placeholder="Who actually granted this, e.g. Michelle Davenport"
                />
              </label>
              <label>
                Granted by's role (optional)
                <input
                  type="text"
                  value={grantedByRole}
                  onChange={(e) => setGrantedByRole(e.currentTarget.value)}
                  placeholder="e.g. Chief Counsel"
                />
              </label>
            </div>
            <label>
              Date granted
              <input type="date" value={grantedDate} onChange={(e) => setGrantedDate(e.currentTarget.value)} />
            </label>
          </>
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
        <label>
          Document reference (optional)
          <input
            type="text"
            value={documentReference}
            onChange={(e) => setDocumentReference(e.currentTarget.value)}
            placeholder="e.g. an email subject line or file reference"
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
