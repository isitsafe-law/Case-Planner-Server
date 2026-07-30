import { useMemo, useState } from 'react'
import { ModalShell } from './App'
import { formatDate } from './ui/format'
import { DISCOVERY_STRATEGIES, type DiscoveryStrategy } from './dashboard/types'

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
  assignedAttorney?: string
  nextAction?: string
  nextReviewDate?: string
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
  assignedAttorney: string
  nextAction: string
  nextReviewDate: string
  discoveryStrategy: string
}

const WORKFLOW_STATUSES = ['Filed / Service Pending', 'Active Litigation', 'Settlement Pending', 'Trial Preparation', 'Resolved / Closed']

export function TriageWizard({
  caseData,
  counties,
  workflowStatuses = WORKFLOW_STATUSES,
  attorneys = [],
  onActivate,
  onClose,
}: {
  caseData: TriageWizardCase
  counties: string[]
  workflowStatuses?: string[]
  attorneys?: string[]
  onActivate: (patch: TriageWizardPatch, options: { discoveryStrategy: string; generateChecklist: boolean; generateDeadlines: boolean }) => Promise<void>
  onClose: () => void
}) {
  const [busy, setBusy] = useState(false)
  const [draft, setDraft] = useState<TriageWizardCase>({ ...caseData })
  const [generateChecklist, setGenerateChecklist] = useState(true)
  const [generateDeadlines, setGenerateDeadlines] = useState(true)

  const isFiled = draft.caseStatus !== 'Pipeline' && draft.caseStatus !== ''
  const mustFix = useMemo(() => {
    const errors: string[] = []
    if (!draft.caseName.trim()) errors.push('Case name is required.')
    if (!draft.jobNumber.trim()) errors.push('Job number is required.')
    if (!draft.tract.trim()) errors.push('Tract is required.')
    if (!draft.county.trim()) errors.push('County is required.')
    if (!draft.caseStatus) errors.push('Choose whether this case is pipeline or filed/active.')
    if (isFiled && !draft.filingDate) errors.push('Filing date is required for a filed case.')
    return errors
  }, [draft, isFiled])

  const warnings = [
    isFiled && !draft.servicePerfected ? 'Service is not perfected; activation will keep a service-pending reminder active.' : '',
    isFiled && (!draft.discoveryStrategy || draft.discoveryStrategy === 'Strategy not selected') ? 'Discovery strategy is undecided; it can be selected later from Discovery.' : '',
    !draft.nextAction ? 'No next action is recorded; the attorney dashboard may show a setup reminder.' : '',
  ].filter(Boolean)

  function patch(): TriageWizardPatch {
    return {
      caseName: draft.caseName,
      caseNumber: draft.caseNumber,
      jobNumber: draft.jobNumber,
      tract: draft.tract,
      county: draft.county,
      caseStatus: draft.caseStatus,
      filingDate: draft.filingDate,
      servicePerfected: draft.servicePerfected,
      servicePerfectedDate: draft.servicePerfected ? draft.servicePerfectedDate : '',
      trialDate: draft.trialDate,
      closedDate: draft.caseStatus === 'Resolved / Closed' ? draft.closedDate : '',
      assignedAttorney: draft.assignedAttorney,
      nextAction: draft.nextAction,
      nextReviewDate: draft.nextReviewDate,
    }
  }

  async function submit() {
    if (mustFix.length > 0) return
    setBusy(true)
    try {
      await onActivate({ ...patch() }, { discoveryStrategy: draft.discoveryStrategy, generateChecklist, generateDeadlines })
    } finally {
      setBusy(false)
    }
  }

  const set = (value: Partial<TriageWizardCase>) => setDraft((current) => ({ ...current, ...value }))

  return (
    <ModalShell title={`Triage and Activate: ${caseData.caseName || 'Imported Case'}`} onClose={onClose}>
      <form className="stacked-form top-gap" onSubmit={(event) => { event.preventDefault(); void submit() }}>
        <p className="helper-text">Review the imported record, complete only the activation fields, and perform the transition once. Optional work can be completed later.</p>

        <section className="triage-section">
          <h3>Imported case</h3>
          <div className="form-grid">
            <label>Case name<input value={draft.caseName} onChange={(e) => set({ caseName: e.currentTarget.value })} /></label>
            <label>Case number<input value={draft.caseNumber} onChange={(e) => set({ caseNumber: e.currentTarget.value })} /></label>
            <label>Job number<input value={draft.jobNumber} onChange={(e) => set({ jobNumber: e.currentTarget.value })} /></label>
            <label>Tract<input value={draft.tract} onChange={(e) => set({ tract: e.currentTarget.value })} /></label>
            <label>County<select value={draft.county} onChange={(e) => set({ county: e.currentTarget.value })}><option value="">Select county</option>{counties.map((county) => <option key={county} value={county}>{county}</option>)}</select></label>
          </div>
        </section>

        <section className="triage-section">
          <h3>Assignment and case position</h3>
          <div className="form-grid">
            <label>Assigned attorney<select value={draft.assignedAttorney} onChange={(e) => set({ assignedAttorney: e.currentTarget.value })}><option value="">Unassigned</option>{attorneys.map((attorney) => <option key={attorney} value={attorney}>{attorney}</option>)}</select></label>
            <label>Case position<select value={draft.caseStatus} onChange={(e) => set({ caseStatus: e.currentTarget.value })}><option value="">Select position</option>{workflowStatuses.map((status) => <option key={status} value={status}>{status}</option>)}</select></label>
          </div>
        </section>

        <section className="triage-section">
          <h3>Filing and service</h3>
          <div className="form-grid">
            <label>Filing date<input type="date" value={draft.filingDate} onChange={(e) => set({ filingDate: e.currentTarget.value })} /></label>
            {isFiled && <label className="toggle-inline"><span>Service perfected</span><input type="checkbox" checked={draft.servicePerfected} onChange={(e) => set({ servicePerfected: e.currentTarget.checked })} /></label>}
            {isFiled && draft.servicePerfected && <label>Service-perfected date<input type="date" value={draft.servicePerfectedDate} onChange={(e) => set({ servicePerfectedDate: e.currentTarget.value })} /></label>}
            {draft.caseStatus === 'Trial Preparation' && <label>Jury trial date<input type="date" value={draft.trialDate} onChange={(e) => set({ trialDate: e.currentTarget.value })} /></label>}
          </div>
        </section>

        {isFiled && <section className="triage-section">
          <h3>Discovery planning <span className="helper-text">optional</span></h3>
          <label>Discovery strategy<select value={draft.discoveryStrategy} onChange={(e) => set({ discoveryStrategy: e.currentTarget.value })}>{DISCOVERY_STRATEGIES.map((strategy) => <option key={strategy} value={strategy}>{strategy}</option>)}</select></label>
          <p className="helper-text">This saves the existing discovery posture during activation. It does not generate discovery work by itself and can be changed later.</p>
        </section>}

        <section className="triage-section">
          <h3>Optional setup</h3>
          <div className="form-grid">
            <label>Next action<input value={draft.nextAction} onChange={(e) => set({ nextAction: e.currentTarget.value })} placeholder="What should happen next?" /></label>
            <label>Follow-up date<input type="date" value={draft.nextReviewDate} onChange={(e) => set({ nextReviewDate: e.currentTarget.value })} /></label>
          </div>
          <label className="toggle-inline"><span>Generate default checklist templates</span><input type="checkbox" checked={generateChecklist} onChange={(e) => setGenerateChecklist(e.currentTarget.checked)} /></label>
          <label className="toggle-inline"><span>Generate deadline templates</span><input type="checkbox" checked={generateDeadlines} onChange={(e) => setGenerateDeadlines(e.currentTarget.checked)} /></label>
        </section>

        <section className="triage-section triage-review">
          <h3>Activation review</h3>
          {mustFix.length > 0 && <><strong>Must fix before activation</strong><ul className="plain-list">{mustFix.map((error) => <li key={error}>{error}</li>)}</ul></>}
          {warnings.length > 0 && <><strong>Can complete later</strong><ul className="plain-list">{warnings.map((warning) => <li key={warning}>{warning}</li>)}</ul></>}
          <ul className="plain-list">
            <li className="list-row"><span>Workflow</span><strong>{draft.caseStatus || 'Not selected'}</strong></li>
            <li className="list-row"><span>Filing date</span><strong>{formatDate(draft.filingDate)}</strong></li>
            {isFiled && <li className="list-row"><span>Service</span><strong>{draft.servicePerfected ? `Perfected${draft.servicePerfectedDate ? ` (${formatDate(draft.servicePerfectedDate)})` : ''}` : 'Pending — warning only'}</strong></li>}
            <li className="list-row"><span>Generated work</span><strong>{[generateChecklist && 'checklist', generateDeadlines && 'deadlines'].filter(Boolean).join(' and ') || 'none'}</strong></li>
          </ul>
        </section>

        <div className="button-row"><button className="primary" type="submit" disabled={busy || mustFix.length > 0}>{busy ? 'Activating…' : 'Save and Activate'}</button><button type="button" onClick={onClose} disabled={busy}>Close (resume later)</button></div>
      </form>
    </ModalShell>
  )
}

export type { DiscoveryStrategy }
