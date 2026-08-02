import { useState } from 'react'

type PreparationCase = { id: number; caseName: string; caseNumber?: string | null; assignedAttorney?: string | null; rowVersion?: string | null }
type PreparationEvent = { id: number; caseId: number; eventType?: string | null; title?: string | null; hearingDate?: string | null; endDate?: string | null; location?: string | null }
type PreparationWork = { id: number; caseId: number; relatedEventId?: number | null; title?: string; task?: string; dueDate?: string | null; status?: string | null; assignedStaffName?: string | null }
type PendingEventChange = { id: number; proposedStartDate: string; proposedEndDate?: string | null; note?: string | null }

export type EventPreparationPageProps = {
  event: PreparationEvent
  caseRecord?: PreparationCase
  work: PreparationWork[]
  onBack: () => void
  onOpenCase: (caseId: number) => void
  onAddTask: () => void
  onAddDeadline: () => void
  onApplyTemplate: () => void | Promise<void>
  onRecalculateDates: () => void | Promise<void>
  onRemindAttorney: () => void | Promise<void>
  onProposeDateChange: (proposedStartDate: string, proposedEndDate: string | null, note: string) => void | Promise<void>
  onGetPendingDateChange: () => Promise<PendingEventChange | null>
  onReviewDateChange: (requestId: number, decision: 'Approved' | 'Rejected') => void | Promise<void>
}

const done = (status?: string | null) => ['Done', 'Complete', 'Completed', 'N/A'].includes(status || '')
const formatDate = (value?: string | null) => value ? new Date(`${value.slice(0, 10)}T00:00:00`).toLocaleDateString() : 'No date'

export function EventPreparationPage({ event, caseRecord, work, onBack, onOpenCase, onAddTask, onAddDeadline, onApplyTemplate, onRecalculateDates, onRemindAttorney, onProposeDateChange, onGetPendingDateChange, onReviewDateChange }: EventPreparationPageProps) {
  const [showDateProposal, setShowDateProposal] = useState(false)
  const [proposedStartDate, setProposedStartDate] = useState(event.hearingDate || '')
  const [proposedEndDate, setProposedEndDate] = useState(event.endDate || '')
  const [proposalNote, setProposalNote] = useState('')
  const [pendingChange, setPendingChange] = useState<PendingEventChange | null>(null)
  const [showPendingReview, setShowPendingReview] = useState(false)
  const linked = work.filter((item) => item.relatedEventId === event.id)
  const active = linked.filter((item) => !done(item.status))
  const completed = linked.filter((item) => done(item.status))
  const today = new Date(); today.setHours(0, 0, 0, 0)
  const overdue = active.filter((item) => item.dueDate && new Date(`${item.dueDate.slice(0, 10)}T00:00:00`) < today)
  const waiting = active.filter((item) => item.assignedStaffName && item.assignedStaffName !== caseRecord?.assignedAttorney)
  const dateText = event.endDate ? `${formatDate(event.hearingDate)} – ${formatDate(event.endDate)}` : formatDate(event.hearingDate)

  return <main className="page event-preparation-page">
    <div className="dash-hd">
      <button className="text-button" onClick={onBack}>← Assistant Dashboard</button>
      <h2>{event.eventType || event.title || 'Event'} Preparation</h2>
      <span className="muted">{caseRecord?.caseName || `Case ${event.caseId}`} · {dateText} · {event.location || 'Location not set'}</span>
    </div>
    <div className="button-row compact-actions preparation-actions">
      <button className="primary" onClick={onOpenCase.bind(null, event.caseId)}>Open Case</button>
      <button onClick={() => void onApplyTemplate()}>Apply available preparation templates</button>
      <button onClick={() => void onRecalculateDates()}>Preview date changes</button>
      <button onClick={() => setShowDateProposal((open) => !open)}>Propose event date change</button>
      <button onClick={async () => { const pending = await onGetPendingDateChange(); setPendingChange(pending); setShowPendingReview(true) }}>Review pending date change</button>
      <button onClick={() => void onRemindAttorney()} disabled={!caseRecord?.assignedAttorney}>Remind Attorney</button>
      <button onClick={onAddTask}>Add Task</button>
      <button onClick={onAddDeadline}>Add Deadline</button>
    </div>
    {showPendingReview && <section className="ui-table-panel event-date-review-panel">
      <div className="panel-hd"><h3>Pending Event Date Change</h3><button className="text-button" onClick={() => setShowPendingReview(false)}>Close</button></div>
      {!pendingChange ? <p className="helper-text">No pending date-change proposal was found.</p> : <>
        <div className="event-date-comparison"><div><span className="metric-label">Confirmed</span><strong>{dateText}</strong></div><div><span className="metric-label">Proposed</span><strong>{pendingChange.proposedEndDate ? `${formatDate(pendingChange.proposedStartDate)} – ${formatDate(pendingChange.proposedEndDate)}` : formatDate(pendingChange.proposedStartDate)}</strong></div></div>
        {pendingChange.note && <p className="helper-text">Note: {pendingChange.note}</p>}
        <div className="button-row compact-actions"><button className="primary" onClick={() => { void onReviewDateChange(pendingChange.id, 'Approved'); setShowPendingReview(false) }}>Approve and recalculate</button><button onClick={() => { void onReviewDateChange(pendingChange.id, 'Rejected'); setShowPendingReview(false) }}>Reject</button></div>
      </>}
    </section>}
    {showDateProposal && <section className="ui-table-panel event-date-proposal-panel">
      <div className="panel-hd"><h3>Propose Event Date Change</h3><button className="text-button" onClick={() => setShowDateProposal(false)}>Cancel</button></div>
      <p className="helper-text">Confirmed: {dateText}. The confirmed date remains unchanged until attorney or manager approval.</p>
      <div className="form-grid compact-form-grid">
        <label>Proposed start date<input type="date" value={proposedStartDate} onChange={(e) => setProposedStartDate(e.currentTarget.value)} required /></label>
        <label>Proposed end date<input type="date" value={proposedEndDate} onChange={(e) => setProposedEndDate(e.currentTarget.value)} /></label>
        <label className="span-2">Reason or note<textarea value={proposalNote} onChange={(e) => setProposalNote(e.currentTarget.value)} rows={2} placeholder="Optional context for the reviewing attorney" /></label>
      </div>
      <div className="button-row compact-actions"><button className="primary" disabled={!proposedStartDate} onClick={() => { void onProposeDateChange(proposedStartDate, proposedEndDate || null, proposalNote); setShowDateProposal(false) }}>Submit proposal</button></div>
    </section>}
    <div className="ui-tiles dashboard-kpi-strip preparation-summary">
      <div className="metric-tile"><span className="metric-label">Open</span><strong>{active.length}</strong></div>
      <div className="metric-tile"><span className="metric-label">Overdue</span><strong>{overdue.length}</strong></div>
      <div className="metric-tile"><span className="metric-label">Waiting on Attorney</span><strong>{waiting.length}</strong></div>
      <div className="metric-tile"><span className="metric-label">Completed</span><strong>{completed.length}</strong></div>
    </div>
    <section className="ui-table-panel">
      <div className="panel-hd"><h3>Active Preparation Work</h3><span className="count">{active.length}</span></div>
      <div className="assistant-list">
        {active.map((item) => <div className="preparation-work-row" key={`${item.task ? 'task' : 'deadline'}-${item.id}`}>
          <span><strong>{item.task || item.title || 'Work item'}</strong><small>{item.assignedStaffName || 'Unassigned'}</small></span>
          <span className={item.dueDate && new Date(`${item.dueDate.slice(0, 10)}T00:00:00`) < today ? 'ui-cell-danger' : undefined}>{formatDate(item.dueDate)}</span>
        </div>)}
        {active.length === 0 && <p className="helper-text">No active work is linked to this proceeding.</p>}
      </div>
    </section>
    <details className="ui-table-panel preparation-completed">
      <summary>Completed work ({completed.length})</summary>
      <div className="assistant-list">
        {completed.map((item) => <div className="preparation-work-row" key={`${item.task ? 'task' : 'deadline'}-${item.id}`}><span><strong>{item.task || item.title || 'Work item'}</strong></span><span>{item.status}</span></div>)}
      </div>
    </details>
  </main>
}
