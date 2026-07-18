import { useState } from 'react'
import type { ActionQueueItem } from './types'
import { MatterPostureSummary } from './MatterPostureSummary'

export type ActionQueueHandlers = {
  onOpenCase: (caseId: number) => void
  /** Legacy compatibility only; no queue control renders this action. */
  onRecordDecision?: (caseId: number) => void
  onSetDiscoveryStrategy?: (caseId: number, strategy: string) => Promise<void>
  onCompleteDeadline?: (caseId: number, deadlineId: number) => Promise<void>
  onUpdateDeadline?: (caseId: number, deadlineId: number, dueDate: string, reason: string) => Promise<void>
  onOpenDiscovery?: (caseId: number) => void
  onSetNextAction: (caseId: number, nextAction: string, reviewDate: string) => Promise<void>
  onMarkWaiting: (caseId: number, waitingOn: string, expectedResponse: string, followUpDate: string) => Promise<void>
  onAddNote: (caseId: number, note: string) => Promise<void>
  onDefer: (caseId: number, reason: string, futureReviewDate: string) => Promise<void>
  onAssignHolder: (caseId: number, holder: string) => Promise<void>
}

const PRIORITY_LABEL: Record<number, string> = { 1: 'Immediate', 2: 'Attorney Decision', 3: 'Momentum', 4: 'Planned Work' }
const PRIORITY_TONE: Record<number, string> = { 1: 'danger', 2: 'warn', 3: 'primary', 4: 'neutral' }

type QuickActionForm = 'plan' | 'nextAction' | 'waiting' | 'note' | 'defer' | 'holder' | 'discovery' | 'deadline' | null

export function ActionQueueItemCard({
  item,
  handlers,
  selected,
  onToggleSelect,
}: {
  item: ActionQueueItem
  handlers: ActionQueueHandlers
  selected?: boolean
  onToggleSelect?: (caseId: number) => void
}) {
  const [openForm, setOpenForm] = useState<QuickActionForm>(null)
  const [nextAction, setNextAction] = useState('')
  const [reviewDate, setReviewDate] = useState(item.reviewDate ?? '')
  const [note, setNote] = useState('')
  const [deferReason, setDeferReason] = useState('')
  const [deferDate, setDeferDate] = useState('')
  const [waitingOn, setWaitingOn] = useState('')
  const [expectedResponse, setExpectedResponse] = useState('')
  const [followUpDate, setFollowUpDate] = useState('')
  const [holder, setHolder] = useState(item.currentHolder ?? '')
  const [discoveryStrategy, setDiscoveryStrategy] = useState('')
  const [deadlineDate, setDeadlineDate] = useState(item.reviewDate ?? '')
  const [deadlineReason, setDeadlineReason] = useState('')
  const [busy, setBusy] = useState(false)

  async function run(action: () => Promise<void>) {
    setBusy(true)
    try {
      await action()
      setOpenForm(null)
    } finally {
      setBusy(false)
    }
  }

  return (
    <li className="action-queue-item">
      {onToggleSelect && (
        <div className="action-queue-item-selection">
          <input type="checkbox" checked={!!selected} onChange={() => onToggleSelect(item.caseId)} aria-label={`Select ${item.caseName}`} />
        </div>
      )}
      <div className="action-queue-item-content">
      <div className="action-queue-item-header">
        <span className={`pill pill-${PRIORITY_TONE[item.priorityLevel]}`}>{item.actionCategory.toUpperCase()}</span>
        <span className="action-queue-item-title">
          {item.caseName}
          {item.caseNumber && <span className="subtle-text"> · {item.caseNumber}</span>}
        </span>
        <span className="pill pill-neutral">{item.currentPhase || 'Phase not set'}</span>
      </div>

      <MatterPostureSummary reason={item.reason} posture={item.postureSummary} nextAction={item.recommendedNextAction} />

      <div className="action-queue-item-meta">
        {item.reviewDate && <span>Review by {item.reviewDate}</span>}
        {item.daysSinceMeaningfulActivity !== null && <span>{item.daysSinceMeaningfulActivity} days since meaningful activity</span>}
        {item.relatedWarningCount > 1 && <span>{item.relatedWarningCount} related warnings</span>}
        {item.jobNumber && <span>Job {item.jobNumber}</span>}
        {item.currentHolder && <span>On {item.currentHolder}'s desk</span>}
        <span className="pill pill-neutral">Priority {item.priorityLevel}: {PRIORITY_LABEL[item.priorityLevel]}</span>
      </div>

      <div className="button-row compact-actions row-actions">
        <button onClick={() => handlers.onOpenCase(item.caseId)}>Open case</button>
        {item.relatedDeadlineId && handlers.onCompleteDeadline && item.reason.toLowerCase().includes('deadline') && <button onClick={() => void run(() => handlers.onCompleteDeadline!(item.caseId, item.relatedDeadlineId!))}>Mark complete</button>}
        {item.relatedDeadlineId && handlers.onUpdateDeadline && item.reason.toLowerCase().includes('deadline') && <button onClick={() => setOpenForm(openForm === 'deadline' ? null : 'deadline')}>Update deadline</button>}
        {item.reason.toLowerCase().includes('strategy not selected') && <button onClick={() => setOpenForm(openForm === 'discovery' ? null : 'discovery')}>Set discovery strategy</button>}
        {item.currentPhase?.toLowerCase().includes('pipeline') && <button onClick={() => setOpenForm(openForm === 'holder' ? null : 'holder')}>Change holder</button>}
        <button onClick={() => setOpenForm(openForm === 'plan' ? null : 'plan')}>Plan next step</button>
        <button onClick={() => setOpenForm(openForm === 'note' ? null : 'note')}>Add note</button>
      </div>

      {openForm === 'plan' && <div className="inline-quick-form plan-next-step-menu"><strong>Plan next step</strong><div className="button-row compact-actions"><button onClick={() => setOpenForm('nextAction')}>Set a next action</button><button onClick={() => setOpenForm('waiting')}>Wait on someone or something</button><button onClick={() => { const date = new Date(); date.setDate(date.getDate() + 30); setDeferDate(date.toISOString().slice(0, 10)); setOpenForm('defer') }}>Revisit in 30 days</button><button onClick={() => { setDeferDate(''); setOpenForm('defer') }}>Choose custom revisit date</button></div></div>}

      {openForm === 'nextAction' && (
        <form
          className="inline-quick-form"
          onSubmit={(e) => {
            e.preventDefault()
            void run(() => handlers.onSetNextAction(item.caseId, nextAction, reviewDate))
          }}
        >
          <label>
            Next action
            <input value={nextAction} onChange={(e) => setNextAction(e.currentTarget.value)} required />
          </label>
          <label>
            Review date
            <input type="date" value={reviewDate} onChange={(e) => setReviewDate(e.currentTarget.value)} required />
          </label>
          <button className="primary" type="submit" disabled={busy}>Save</button>
        </form>
      )}

      {openForm === 'waiting' && (
        <form
          className="inline-quick-form"
          onSubmit={(e) => {
            e.preventDefault()
            void run(() => handlers.onMarkWaiting(item.caseId, waitingOn, expectedResponse, followUpDate))
          }}
        >
          <label>
            Waiting on
            <input value={waitingOn} onChange={(e) => setWaitingOn(e.currentTarget.value)} placeholder="e.g. Owner's appraiser to respond" required />
          </label>
          <label>
            Expected response or event
            <input value={expectedResponse} onChange={(e) => setExpectedResponse(e.currentTarget.value)} />
          </label>
          <label>
            Follow-up date
            <input type="date" value={followUpDate} onChange={(e) => setFollowUpDate(e.currentTarget.value)} required />
          </label>
          <button className="primary" type="submit" disabled={busy}>Save</button>
        </form>
      )}

      {openForm === 'note' && (
        <form
          className="inline-quick-form"
          onSubmit={(e) => {
            e.preventDefault()
            void run(() => handlers.onAddNote(item.caseId, note))
          }}
        >
          <label>
            Short note
            <input value={note} onChange={(e) => setNote(e.currentTarget.value)} required />
          </label>
          <button className="primary" type="submit" disabled={busy}>Save</button>
        </form>
      )}

      {openForm === 'defer' && (
        <form
          className="inline-quick-form"
          onSubmit={(e) => {
            e.preventDefault()
            void run(() => handlers.onDefer(item.caseId, deferReason, deferDate))
          }}
        >
          <label>
            Reason
            <input value={deferReason} onChange={(e) => setDeferReason(e.currentTarget.value)} placeholder="Optional" />
          </label>
          <label>
            Future review date
            <input type="date" value={deferDate} onChange={(e) => setDeferDate(e.currentTarget.value)} required />
          </label>
          <button className="primary" type="submit" disabled={busy}>Defer</button>
        </form>
      )}

      {openForm === 'holder' && (
        <form
          className="inline-quick-form"
          onSubmit={(e) => {
            e.preventDefault()
            void run(() => handlers.onAssignHolder(item.caseId, holder))
          }}
        >
          <label>
            Current holder
            <select value={holder} onChange={(e) => setHolder(e.currentTarget.value)} required>
              <option value="">Select...</option>
              <option value="Legal Assistant">Legal Assistant</option>
              <option value="Attorney">Attorney</option>
              <option value="Deputy Chief Counsel">Deputy Chief Counsel</option>
              <option value="Chief Counsel">Chief Counsel</option>
              <option value="Filing Staff">Filing Staff</option>
              <option value="Other">Other</option>
            </select>
          </label>
          <button className="primary" type="submit" disabled={busy}>Save</button>
        </form>
      )}

      {openForm === 'discovery' && (
        <form className="inline-quick-form" onSubmit={(e) => { e.preventDefault(); if (handlers.onSetDiscoveryStrategy && discoveryStrategy) void run(() => handlers.onSetDiscoveryStrategy!(item.caseId, discoveryStrategy)) }}>
          <label>Discovery strategy<select value={discoveryStrategy} onChange={(e) => setDiscoveryStrategy(e.currentTarget.value)} required><option value="">Select strategy...</option><option>Written discovery first</option><option>Landowner deposition first</option><option>Appraiser discovery first</option><option>Limited targeted discovery</option><option>Full discovery plan</option><option>No discovery currently needed</option><option>Strategy deferred until a stated event</option></select></label>
          <button className="primary" type="submit" disabled={busy || !handlers.onSetDiscoveryStrategy}>Save</button>
        </form>
      )}

      {openForm === 'deadline' && <form className="inline-quick-form" onSubmit={(e) => { e.preventDefault(); if (handlers.onUpdateDeadline && deadlineDate) void run(() => handlers.onUpdateDeadline!(item.caseId, item.relatedDeadlineId!, deadlineDate, deadlineReason)) }}><label>New due date<input type="date" value={deadlineDate} onChange={(e) => setDeadlineDate(e.currentTarget.value)} required /></label><label>Reason<input value={deadlineReason} onChange={(e) => setDeadlineReason(e.currentTarget.value)} placeholder="Why is the date changing?" required /></label><button className="primary" type="submit" disabled={busy || !handlers.onUpdateDeadline}>Save</button></form>}
      </div>
    </li>
  )
}
