import { useState } from 'react'
import { ModalShell } from '../App'

export const ACTIVITY_TYPE_GROUPS: { label: string; types: string[] }[] = [
  { label: 'Filing & Service', types: ['ComplaintFiled', 'AnswerFiled', 'ServiceCompleted', 'PublicationCompleted'] },
  { label: 'Discovery', types: ['DiscoveryServed', 'DiscoveryResponsesReceived', 'DiscoveryResponsesReviewed', 'DepositionHeld'] },
  { label: 'Valuation', types: ['AppraisalReceived', 'AppraisalReviewed'] },
  { label: 'Negotiation & Settlement', types: ['NegotiationPositionChanged', 'SettlementAuthorityRequested', 'SettlementAuthorityReceived'] },
  { label: 'Motions & Mediation', types: ['MotionFiled', 'MotionDecided', 'MediationScheduled', 'MediationHeld'] },
  { label: 'Trial Prep', types: ['TrialPrepMilestoneCompleted'] },
]

export function activityTypeLabel(t: string) {
  return t.replace(/([A-Z])/g, ' $1').trim()
}

export function RecordDecisionDialog({
  caseName,
  onClose,
  onSubmit,
}: {
  caseName: string
  onClose: () => void
  onSubmit: (payload: { activityType: string; notes: string }) => Promise<void>
}) {
  const [activityType, setActivityType] = useState('AttorneyStrategyDecisionRecorded')
  const [notes, setNotes] = useState('')
  const [busy, setBusy] = useState(false)

  return (
    <ModalShell title={`Record Decision: ${caseName}`} onClose={onClose}>
      <form
        className="stacked-form"
        onSubmit={async (e) => {
          e.preventDefault()
          setBusy(true)
          try {
            await onSubmit({ activityType, notes })
          } finally {
            setBusy(false)
          }
        }}
      >
        <label>
          What happened
          <select value={activityType} onChange={(e) => setActivityType(e.currentTarget.value)}>
            <option value="AttorneyStrategyDecisionRecorded">{activityTypeLabel('AttorneyStrategyDecisionRecorded')}</option>
            {ACTIVITY_TYPE_GROUPS.map((group) => (
              <optgroup key={group.label} label={group.label}>
                {group.types.map((t) => <option key={t} value={t}>{activityTypeLabel(t)}</option>)}
              </optgroup>
            ))}
          </select>
        </label>
        <label>
          Notes
          <textarea value={notes} onChange={(e) => setNotes(e.currentTarget.value)} rows={3} placeholder="What was decided and why" />
        </label>
        <div className="button-row">
          <button className="primary" type="submit" disabled={busy}>Record</button>
          <button type="button" onClick={onClose}>Cancel</button>
        </div>
      </form>
    </ModalShell>
  )
}
