import { useState } from 'react'
import { ModalShell } from '../App'

export const ACTIVITY_TYPE_GROUPS: { label: string; types: string[] }[] = [
  { label: 'Filing & Service', types: ['ComplaintFiled', 'AnswerFiled', 'ServiceCompleted', 'PublicationCompleted'] },
  { label: 'Discovery', types: ['DiscoveryServed', 'DiscoveryResponsesReceived', 'DiscoveryResponsesReviewed', 'DepositionHeld'] },
  { label: 'Valuation', types: ['AppraisalReceived', 'AppraisalReviewed'] },
  { label: 'Negotiation & Settlement', types: ['NegotiationPositionChanged'] },
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
  fieldChanged,
  previousValue,
  newValue,
}: {
  caseName: string
  onClose: () => void
  onSubmit: (payload: {
    activityType: string
    notes: string
    fieldChanged?: string
    previousValue?: string
    newValue?: string
  }) => Promise<void>
  // When provided (a manager-override call path in a later milestone), the dialog shows a read-only
  // "what's changing" summary and requires notes/reason before submit. Absent by default, in which
  // case this component's behavior and appearance are unchanged from before these props existed.
  fieldChanged?: string
  previousValue?: string
  newValue?: string
}) {
  const [activityType, setActivityType] = useState('AttorneyStrategyDecisionRecorded')
  const [notes, setNotes] = useState('')
  const [busy, setBusy] = useState(false)
  const isFieldChange = fieldChanged !== undefined || previousValue !== undefined || newValue !== undefined
  const notesRequired = isFieldChange && notes.trim() === ''

  return (
    <ModalShell title={`Record Decision: ${caseName}`} onClose={onClose}>
      <form
        className="stacked-form"
        onSubmit={async (e) => {
          e.preventDefault()
          if (notesRequired) return
          setBusy(true)
          try {
            await onSubmit({ activityType, notes, fieldChanged, previousValue, newValue })
          } finally {
            setBusy(false)
          }
        }}
      >
        {isFieldChange && (
          <p className="field-change-summary">
            Changing {fieldChanged}: {previousValue ?? '(none)'} &rarr; {newValue ?? '(none)'}
          </p>
        )}
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
          Notes{isFieldChange ? ' (required)' : ''}
          <textarea value={notes} onChange={(e) => setNotes(e.currentTarget.value)} rows={3} placeholder="What was decided and why" required={isFieldChange} />
        </label>
        <div className="button-row">
          <button className="primary" type="submit" disabled={busy || notesRequired}>Record</button>
          <button type="button" onClick={onClose}>Cancel</button>
        </div>
      </form>
    </ModalShell>
  )
}
