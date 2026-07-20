import { useState } from 'react'
import type { ActionCategory, ActionQueueItem } from './types'
import { Btn } from '../ui/Btn'
import { StatusChip, type StatusTone } from '../ui/StatusChip'
import { HolderChip } from '../ui/HolderChip'
import { formatDate as displayDate } from '../ui/format'

const CATEGORY_TONE: Record<ActionCategory, StatusTone> = {
  Decide: 'warn',
  Act: 'primary',
  Review: 'neutral',
  Escalate: 'danger',
  Prepare: 'neutral',
}

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

const PRIORITY_STRIPE: Record<number, string> = { 1: 'p1', 2: 'p2', 3: 'p3', 4: 'p4' }

type QuickActionForm = 'plan' | 'nextAction' | 'waiting' | 'note' | 'defer' | 'holder' | 'discovery' | 'deadline' | null

// Dense-table replacement for the old ActionQueueItemCard: a stripe-and-checkbox main row plus an
// optional full-width expansion row directly beneath it for whichever quick-action form is open.
// All eight quick-action forms (plan menu, next action, waiting, note, defer, holder, discovery
// strategy, deadline update) carry over field-for-field from the card version.
export function ActionQueueRow({
  item,
  handlers,
  selected,
  onToggleSelect,
  county,
}: {
  item: ActionQueueItem
  handlers: ActionQueueHandlers
  selected: boolean
  onToggleSelect: (caseId: number) => void
  county?: string | null
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
    <>
      <tr className={selected ? 'ui-row-sel' : ''}>
        <td className={`ui-stripe-cell ${PRIORITY_STRIPE[item.priorityLevel] || ''}`}>
          <input type="checkbox" checked={selected} onChange={() => onToggleSelect(item.caseId)} aria-label={`Select ${item.caseName}`} />
        </td>
        <td>
          <button className="ui-case-link" onClick={() => handlers.onOpenCase(item.caseId)}>{item.caseName}</button>
          <div className="ui-sub ui-data">{[item.caseNumber, county].filter(Boolean).join(' · ') || '—'}</div>
        </td>
        <td>
          <div className="ui-queue-reason">
            <StatusChip tone={CATEGORY_TONE[item.actionCategory]}>{item.actionCategory}</StatusChip>
            <span>{item.reason}</span>
          </div>
        </td>
        <td className="ui-data">{item.reviewDate ? displayDate(item.reviewDate) : <span className="ui-cell-faint">—</span>}</td>
      </tr>

      <tr className="ui-queue-detail-row">
        <td></td>
        <td colSpan={3}>
          {item.postureSummary && <div className="ui-sub">{item.postureSummary}</div>}
          {item.recommendedNextAction && <div className="ui-sub ui-sub-recommend">Recommended: {item.recommendedNextAction}</div>}
          {(item.currentHolder || item.daysSinceMeaningfulActivity !== null || item.relatedWarningCount > 1) && (
            <div className="ui-row-actions ui-row-actions-wrap ui-queue-meta">
              {item.currentHolder && <HolderChip holder={item.currentHolder} />}
              {item.daysSinceMeaningfulActivity !== null && <StatusChip tone="neutral">{item.daysSinceMeaningfulActivity} days inactive</StatusChip>}
              {item.relatedWarningCount > 1 && <StatusChip tone="warn">{item.relatedWarningCount} related warnings</StatusChip>}
            </div>
          )}
          <div className="ui-row-actions ui-row-actions-wrap">
            <Btn size="sm" onClick={() => handlers.onOpenCase(item.caseId)}>Open case</Btn>
            {item.relatedDeadlineId != null && handlers.onCompleteDeadline && item.reason.toLowerCase().includes('deadline') && (
              <Btn size="sm" onClick={() => void run(() => handlers.onCompleteDeadline!(item.caseId, item.relatedDeadlineId!))}>Mark complete</Btn>
            )}
            {item.relatedDeadlineId != null && handlers.onUpdateDeadline && item.reason.toLowerCase().includes('deadline') && (
              <Btn size="sm" onClick={() => setOpenForm(openForm === 'deadline' ? null : 'deadline')}>Update deadline</Btn>
            )}
            {item.reason.toLowerCase().includes('strategy not selected') && (
              <Btn size="sm" onClick={() => setOpenForm(openForm === 'discovery' ? null : 'discovery')}>Set discovery strategy</Btn>
            )}
            {item.currentPhase?.toLowerCase().includes('pipeline') && (
              <Btn size="sm" onClick={() => setOpenForm(openForm === 'holder' ? null : 'holder')}>Change holder</Btn>
            )}
            <Btn size="sm" onClick={() => setOpenForm(openForm === 'plan' ? null : 'plan')}>Plan next step</Btn>
            <Btn size="sm" onClick={() => setOpenForm(openForm === 'note' ? null : 'note')}>Add note</Btn>
          </div>
        </td>
      </tr>

      {openForm && (
        <tr className="ui-expand-row">
          <td></td>
          <td colSpan={3}>
            {openForm === 'plan' && (
              <div className="inline-quick-form plan-next-step-menu">
                <strong>Plan next step</strong>
                <div className="ui-row-actions ui-row-actions-wrap">
                  <Btn size="sm" onClick={() => setOpenForm('nextAction')}>Set a next action</Btn>
                  <Btn size="sm" onClick={() => setOpenForm('waiting')}>Wait on someone or something</Btn>
                  <Btn size="sm" onClick={() => { const date = new Date(); date.setDate(date.getDate() + 30); setDeferDate(date.toISOString().slice(0, 10)); setOpenForm('defer') }}>Revisit in 30 days</Btn>
                  <Btn size="sm" onClick={() => { setDeferDate(''); setOpenForm('defer') }}>Choose custom revisit date</Btn>
                </div>
              </div>
            )}

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
                <Btn size="sm" variant="primary" type="submit" disabled={busy}>Save</Btn>
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
                <Btn size="sm" variant="primary" type="submit" disabled={busy}>Save</Btn>
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
                <Btn size="sm" variant="primary" type="submit" disabled={busy}>Save</Btn>
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
                <Btn size="sm" variant="primary" type="submit" disabled={busy}>Defer</Btn>
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
                <Btn size="sm" variant="primary" type="submit" disabled={busy}>Save</Btn>
              </form>
            )}

            {openForm === 'discovery' && (
              <form
                className="inline-quick-form"
                onSubmit={(e) => {
                  e.preventDefault()
                  if (handlers.onSetDiscoveryStrategy && discoveryStrategy) void run(() => handlers.onSetDiscoveryStrategy!(item.caseId, discoveryStrategy))
                }}
              >
                <label>
                  Discovery strategy
                  <select value={discoveryStrategy} onChange={(e) => setDiscoveryStrategy(e.currentTarget.value)} required>
                    <option value="">Select strategy...</option>
                    <option>Written discovery first</option>
                    <option>Landowner deposition first</option>
                    <option>Appraiser discovery first</option>
                    <option>Limited targeted discovery</option>
                    <option>Full discovery plan</option>
                    <option>No discovery currently needed</option>
                    <option>Strategy deferred until a stated event</option>
                  </select>
                </label>
                <Btn size="sm" variant="primary" type="submit" disabled={busy || !handlers.onSetDiscoveryStrategy}>Save</Btn>
              </form>
            )}

            {openForm === 'deadline' && (
              <form
                className="inline-quick-form"
                onSubmit={(e) => {
                  e.preventDefault()
                  if (handlers.onUpdateDeadline && deadlineDate) void run(() => handlers.onUpdateDeadline!(item.caseId, item.relatedDeadlineId!, deadlineDate, deadlineReason))
                }}
              >
                <label>
                  New due date
                  <input type="date" value={deadlineDate} onChange={(e) => setDeadlineDate(e.currentTarget.value)} required />
                </label>
                <label>
                  Reason
                  <input value={deadlineReason} onChange={(e) => setDeadlineReason(e.currentTarget.value)} placeholder="Why is the date changing?" required />
                </label>
                <Btn size="sm" variant="primary" type="submit" disabled={busy || !handlers.onUpdateDeadline}>Save</Btn>
              </form>
            )}
          </td>
        </tr>
      )}
    </>
  )
}
