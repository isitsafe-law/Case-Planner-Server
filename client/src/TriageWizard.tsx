import { useState } from 'react'
import { ModalShell } from './App'

// Fields the triage wizard confirms/backfills - all assignable to Partial<CaseRecord>.
export type TriageWizardPatch = {
  caseName?: string
  caseNumber?: string
  jobNumber?: string
  tract?: string
  county?: string
  caseStatus?: string
  filingDate?: string
  servicePerfected?: boolean
  servicePerfectedDate?: string
  trialDate?: string
  closedDate?: string
}

export type TriageWizardCase = {
  caseName: string
  caseNumber: string
  jobNumber: string
  tract: string
  county: string
  caseStatus: string
  filingDate: string
  servicePerfected: boolean
  servicePerfectedDate: string
  trialDate: string
  closedDate: string
}

const STEP_TITLES = ['Identifiers', 'Workflow Status', 'Historical Dates', 'Activate']
const WORKFLOW_STATUSES = ['Filed / Service Pending', 'Active Litigation', 'Settlement Pending', 'Trial Preparation', 'Resolved / Closed']

// Walks a newly imported case through confirmation of its identifiers, consolidated workflow status,
// and - critically - the historical dates for stages it has already passed, so activating it
// generates deadlines against real anchors instead of treating decade-old filings as new.
// Every "Next" persists that step's fields (save-and-resume: closing mid-way loses nothing;
// the case stays in Triage until Activate).
export function TriageWizard({
  caseData,
  counties,
  workflowStatuses = WORKFLOW_STATUSES,
  onSaveStep,
  onActivate,
  onClose,
}: {
  caseData: TriageWizardCase
  counties: string[]
  workflowStatuses?: string[]
  onSaveStep: (patch: TriageWizardPatch) => Promise<void>
  onActivate: () => Promise<void>
  onClose: () => void
}) {
  const [step, setStep] = useState(0)
  const [busy, setBusy] = useState(false)
  const [draft, setDraft] = useState<TriageWizardCase>({ ...caseData })

  const atOrPastService = draft.caseStatus !== 'Pipeline'

  function patchForStep(current: number): TriageWizardPatch {
    switch (current) {
      case 0:
        return { caseName: draft.caseName, caseNumber: draft.caseNumber, jobNumber: draft.jobNumber, tract: draft.tract, county: draft.county }
      case 1:
        return { caseStatus: draft.caseStatus }
      case 2:
        return {
          filingDate: draft.filingDate,
          servicePerfected: draft.servicePerfected,
          servicePerfectedDate: draft.servicePerfected ? draft.servicePerfectedDate : '',
          trialDate: draft.trialDate,
          closedDate: draft.caseStatus === 'Resolved / Closed' ? draft.closedDate : '',
        }
      default:
        return {}
    }
  }

  async function saveAndAdvance() {
    setBusy(true)
    try {
      await onSaveStep(patchForStep(step))
      setStep(step + 1)
    } finally {
      setBusy(false)
    }
  }

  async function activate() {
    setBusy(true)
    try {
      await onActivate()
    } finally {
      setBusy(false)
    }
  }

  return (
    <ModalShell title={`Triage: ${caseData.caseName || 'Imported Case'}`} onClose={onClose}>
      <div className="chip-row">
        {STEP_TITLES.map((title, index) => (
          <span key={title} className={index === step ? 'chip active' : 'chip'}>{index + 1}. {title}</span>
        ))}
      </div>

      <form
        className="stacked-form top-gap"
        onSubmit={(e) => {
          e.preventDefault()
          void (step < STEP_TITLES.length - 1 ? saveAndAdvance() : activate())
        }}
      >
        {step === 0 && (
          <>
            <p className="helper-text">Confirm or correct the identifiers pulled in by the import.</p>
            <label>
              Case name
              <input value={draft.caseName} onChange={(e) => setDraft({ ...draft, caseName: e.currentTarget.value })} required />
            </label>
            <label>
              Case number
              <input value={draft.caseNumber} onChange={(e) => setDraft({ ...draft, caseNumber: e.currentTarget.value })} />
            </label>
            <label>
              Job number
              <input value={draft.jobNumber} onChange={(e) => setDraft({ ...draft, jobNumber: e.currentTarget.value })} />
            </label>
            <label>
              Tract
              <input value={draft.tract} onChange={(e) => setDraft({ ...draft, tract: e.currentTarget.value })} />
            </label>
            <label>
              County
              <select value={draft.county} onChange={(e) => setDraft({ ...draft, county: e.currentTarget.value })}>
                <option value="">Select county</option>
                {counties.map((county) => <option key={county} value={county}>{county}</option>)}
              </select>
            </label>
          </>
        )}

        {step === 1 && (
          <>
            <p className="helper-text">Which consolidated workflow status best describes this imported case today? Historical dates are captured next; no live deadlines are generated until activation.</p>
            {workflowStatuses.map((status) => (
              <label key={status} className="toggle-inline">
                <span>{status}</span>
                <input type="radio" name="triage-status" checked={draft.caseStatus === status} onChange={() => setDraft({ ...draft, caseStatus: status })} />
              </label>
            ))}
          </>
        )}

        {step === 2 && (
          <>
            <p className="helper-text">
              These are historical facts being backfilled, not new events - they anchor the deadline engine so
              already-passed reminders are recorded as done instead of firing as overdue.
            </p>
            <label>
              Filing date
              <input type="date" value={draft.filingDate} onChange={(e) => setDraft({ ...draft, filingDate: e.currentTarget.value })} />
            </label>
            {atOrPastService && (
              <>
                <label className="toggle-inline">
                  <span>Service was perfected</span>
                  <input type="checkbox" checked={draft.servicePerfected} onChange={(e) => setDraft({ ...draft, servicePerfected: e.currentTarget.checked })} />
                </label>
                {draft.servicePerfected && (
                  <label>
                    Service perfected date (if known)
                    <input type="date" value={draft.servicePerfectedDate} onChange={(e) => setDraft({ ...draft, servicePerfectedDate: e.currentTarget.value })} />
                  </label>
                )}
              </>
            )}
            {draft.caseStatus === 'Trial Preparation' && (
              <label>
                Trial / hearing date (if set)
                <input type="date" value={draft.trialDate} onChange={(e) => setDraft({ ...draft, trialDate: e.currentTarget.value })} />
              </label>
            )}
            {draft.caseStatus === 'Resolved / Closed' && (
              <label>
                Closed / resolved date
                <input type="date" value={draft.closedDate} onChange={(e) => setDraft({ ...draft, closedDate: e.currentTarget.value })} />
              </label>
            )}
          </>
        )}

        {step === 3 && (
          <>
            <p className="helper-text">Activating starts live tracking - deadline and alert generation begin from these confirmed facts.</p>
            <ul className="plain-list">
              <li className="list-row"><span>Case</span><strong>{draft.caseName}</strong></li>
              <li className="list-row"><span>Workflow status</span><strong>{draft.caseStatus || 'Not set'}</strong></li>
              <li className="list-row"><span>Filing date</span><strong>{draft.filingDate || 'Not set'}</strong></li>
              {atOrPastService && <li className="list-row"><span>Service</span><strong>{draft.servicePerfected ? `Perfected${draft.servicePerfectedDate ? ` (${draft.servicePerfectedDate})` : ''}` : 'Not perfected'}</strong></li>}
            </ul>
          </>
        )}

        <div className="button-row">
          {step > 0 && <button type="button" onClick={() => setStep(step - 1)} disabled={busy}>Back</button>}
          {step < STEP_TITLES.length - 1 ? (
            <button className="primary" type="submit" disabled={busy || (step === 1 && !draft.caseStatus)}>
              {busy ? 'Saving…' : 'Save & Continue'}
            </button>
          ) : (
            <button className="primary" type="submit" disabled={busy}>{busy ? 'Activating…' : 'Activate Case'}</button>
          )}
          <button type="button" onClick={onClose} disabled={busy}>Close (resume later)</button>
        </div>
      </form>
    </ModalShell>
  )
}
