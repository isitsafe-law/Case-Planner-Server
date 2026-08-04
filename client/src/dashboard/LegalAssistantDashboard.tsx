import { useState } from 'react'

type AssistantCase = {
  id: number
  caseName: string
  caseNumber?: string | null
  county?: string | null
  caseStatus?: string | null
  currentHolder?: string | null
  pipelineStage?: string | null
  assignedAttorney?: string | null
  filingDate?: string | null
  servicePerfected?: boolean
  serviceDeadline120?: string | null
}

type AssistantWork = {
  id: number
  caseId: number
  relatedEventId?: number | null
  title?: string
  task?: string
  dueDate?: string | null
  status?: string | null
  assignedStaffName?: string | null
  // Legal Assistant view, phase 2: "Attorney" | "LegalAssistant" | "Either" (default) - a task
  // classified Attorney-only is excluded from this dashboard's queues below (see visibleWork).
  // Deadlines don't carry this field (only checklist items do), so it's undefined for those - the
  // filter treats undefined the same as "Either".
  ownerRole?: string
}

type AssistantEvent = {
  id: number
  caseId: number
  eventType?: string | null
  title?: string | null
  hearingDate?: string | null
  endDate?: string | null
  location?: string | null
  pendingChange?: boolean
}

export type LegalAssistantDashboardProps = {
  assistantName?: string | null
  supportedAttorneyNames?: string[]
  workOwnerNames?: string[]
  cases: AssistantCase[]
  work: AssistantWork[]
  events: AssistantEvent[]
  onOpenCase: (caseId: number) => void
  onOpenPreparation: (eventId: number) => void
  onAssignWork?: (item: AssistantWork, assignee: string | null) => void
}

const openStatus = (value?: string | null) => !['Done', 'Complete', 'Completed', 'N/A'].includes(value || '')
const dateValue = (value?: string | null) => value ? new Date(`${value.slice(0, 10)}T00:00:00`) : null
const dateLabel = (value?: string | null) => value ? new Date(`${value.slice(0, 10)}T00:00:00`).toLocaleDateString() : 'No date'

export function LegalAssistantDashboard({ assistantName, supportedAttorneyNames = [], workOwnerNames = [], cases, work, events, onOpenCase, onOpenPreparation, onAssignWork }: LegalAssistantDashboardProps) {
  const [selectedAttorney, setSelectedAttorney] = useState('All')
  const [horizonDays, setHorizonDays] = useState<number | 'all'>(180)
  const visibleAttorneyNames = supportedAttorneyNames.filter((name, index, list) => name && list.indexOf(name) === index)
  const scopedCases = selectedAttorney === 'All' ? cases : cases.filter((item) => item.assignedAttorney === selectedAttorney)
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  const supportedCaseIds = new Set(scopedCases.map((item) => item.id))
  const visibleWork = work.filter((item) => supportedCaseIds.has(item.caseId) && openStatus(item.status) && item.ownerRole !== 'Attorney')
  const onDesk = visibleWork.filter((item) => !item.assignedStaffName || item.assignedStaffName === assistantName)
  const overdue = visibleWork.filter((item) => { const due = dateValue(item.dueDate); return due && due < today })
  const waitingAttorney = scopedCases.filter((item) => item.caseStatus === 'Pipeline' && ['Attorney', 'Deputy Chief Counsel', 'Chief Counsel'].includes(item.currentHolder || ''))
  const horizonEnd = horizonDays === 'all' ? null : new Date(today.getTime() + horizonDays * 86400000)
  const upcoming = events.filter((item) => { const date = dateValue(item.endDate || item.hearingDate); return supportedCaseIds.has(item.caseId) && date && date >= today && (!horizonEnd || date <= horizonEnd) }).sort((a, b) => (a.hearingDate || '').localeCompare(b.hearingDate || '')).slice(0, 8)
  const service = scopedCases.filter((item) => item.caseStatus === 'Filed / Service Pending' && !item.servicePerfected)
  const ownerOptions = workOwnerNames.filter((name, index, list) => name && list.indexOf(name) === index)

  function renderOwnerControl(item: AssistantWork) {
    if (!onAssignWork) return <small>{item.assignedStaffName || 'Unassigned'}</small>
    return <select className="inline-edit-select assistant-owner-select" aria-label={'Assign ' + (item.task || item.title || 'work item')} value={item.assignedStaffName || ''} onChange={(event) => onAssignWork(item, event.target.value || null)}>
      <option value="">Unassigned</option>
      {ownerOptions.map((name) => <option key={name} value={name}>{name}</option>)}
      {item.assignedStaffName && !ownerOptions.includes(item.assignedStaffName) && <option value={item.assignedStaffName}>{item.assignedStaffName} (current)</option>}
    </select>
  }

  return (
    <main className="page legal-assistant-dashboard">
      <div className="dash-hd">
        <h2>{assistantName ? `${assistantName}'s Assistant Dashboard` : 'Legal Assistant Dashboard'}</h2>
        <span className="muted">Operational work for supported attorneys</span>
        <div className="assistant-dashboard-filters">
          {visibleAttorneyNames.length > 0 && <label><span>Attorney</span><select value={selectedAttorney} onChange={(event) => setSelectedAttorney(event.target.value)}><option value="All">All supported attorneys</option>{visibleAttorneyNames.map((name) => <option key={name} value={name}>{name}</option>)}</select></label>}
          <label><span>Proceedings</span><select value={horizonDays} onChange={(event) => setHorizonDays(event.target.value === 'all' ? 'all' : Number(event.target.value))}><option value={30}>30 days</option><option value={60}>60 days</option><option value={90}>90 days</option><option value={120}>120 days</option><option value={180}>180 days</option><option value="all">All upcoming</option></select></label>
        </div>
      </div>

      <div className="ui-tiles dashboard-kpi-strip" style={{ marginBottom: '1rem' }}>
        <div className="metric-tile"><span className="metric-label">On My Desk</span><strong>{onDesk.length}</strong><small>Open assigned work</small></div>
        <div className="metric-tile"><span className="metric-label">Waiting on Attorney</span><strong>{waitingAttorney.length}</strong><small>Pipeline review or direction</small></div>
        <div className="metric-tile"><span className="metric-label">Upcoming Proceedings</span><strong>{upcoming.length}</strong><small>Next 180 days shown</small></div>
        <div className="metric-tile"><span className="metric-label">Preparation Needs Attention</span><strong>{overdue.length}</strong><small>Overdue tracked work</small></div>
        <div className="metric-tile"><span className="metric-label">Service Follow-Up</span><strong>{service.length}</strong><small>Filed matters not perfected</small></div>
      </div>

      <div className="dashboard-card-grid assistant-dashboard-grid">
        <section className="ui-table-panel">
          <div className="panel-hd"><h3>Upcoming Proceedings and Preparation</h3><span className="count">{horizonDays === 'all' ? 'All upcoming' : horizonDays + '-day view'}</span></div>
          <div className="assistant-list">
            {upcoming.length === 0 ? <p className="helper-text">No upcoming proceedings for supported attorneys.</p> : upcoming.map((event) => {
              const item = cases.find((candidate) => candidate.id === event.caseId)
              const linked = visibleWork.filter((workItem) => workItem.relatedEventId === event.id)
              const linkedOverdue = linked.filter((workItem) => { const due = dateValue(workItem.dueDate); return due && due < today })
              const linkedWaiting = linked.filter((workItem) => workItem.assignedStaffName && workItem.assignedStaffName !== assistantName)
              return <button className="assistant-list-row" key={event.id} onClick={() => onOpenPreparation(event.id)}>
                {event.pendingChange && <em className="pending-event-pill">Date proposal pending</em>}
                <span><strong>{event.eventType || event.title || 'Event'}</strong><small>{dateLabel(event.hearingDate)}{event.endDate ? ` – ${dateLabel(event.endDate)}` : ''} · {event.location || 'Location not set'}</small></span>
                <span><strong>{item?.caseName || `Case ${event.caseId}`}</strong><small>{item?.assignedAttorney || 'Attorney not assigned'} · {linked.length} open · {linkedOverdue.length} overdue · {linkedWaiting.length} waiting</small></span>
              </button>
            })}
          </div>
        </section>

        <section className="ui-table-panel">
          <div className="panel-hd"><h3>Assistant Work Ownership</h3><span className="count">{visibleWork.length} open</span></div>
          <div className="assistant-list">
            {visibleWork.slice(0, 8).map((item) => <div className="assistant-list-row assistant-work-owner-row" key={(item.task ? 'task-' : 'deadline-') + item.id}>
              <button className="assistant-row-link" onClick={() => onOpenCase(item.caseId)}><span><strong>{item.task || item.title || 'Work item'}</strong><small>{cases.find((candidate) => candidate.id === item.caseId)?.caseName || ('Case ' + item.caseId)} · {item.dueDate ? 'Due ' + dateLabel(item.dueDate) : 'No due date'}</small></span></button>
              <span><small>Owner</small>{renderOwnerControl(item)}</span>
            </div>)}
            {visibleWork.length === 0 && <p className="helper-text">No open assistant work is currently in scope.</p>}
          </div>
        </section>

        <section className="ui-table-panel">
          <div className="panel-hd"><h3>Pre-Filing and Document Preparation</h3><span className="count">{scopedCases.filter((item) => item.caseStatus === 'Pipeline').length} cases</span></div>
          <div className="assistant-list">
            {scopedCases.filter((item) => item.caseStatus === 'Pipeline').slice(0, 8).map((item) => <button className="assistant-list-row" key={item.id} onClick={() => onOpenCase(item.id)}><span><strong>{item.caseName}</strong><small>{item.caseNumber || item.county || 'Pipeline case'}</small></span><span><strong>{item.currentHolder || 'Unassigned'}</strong><small>{item.pipelineStage || 'Review stage not set'}</small></span></button>)}
            {scopedCases.every((item) => item.caseStatus !== 'Pipeline') && <p className="helper-text">No supported cases are currently in pipeline.</p>}
          </div>
        </section>

        <section className="ui-table-panel">
          <div className="panel-hd"><h3>Overdue Assistant Work</h3><span className="count">{overdue.length}</span></div>
          <div className="assistant-list">
            {overdue.slice(0, 8).map((item) => <button className="assistant-list-row" key={`${item.task ? 'task' : 'deadline'}-${item.id}`} onClick={() => onOpenCase(item.caseId)}><span><strong>{item.task || item.title || 'Work item'}</strong><small>Due {dateLabel(item.dueDate)}</small></span><span>{cases.find((candidate) => candidate.id === item.caseId)?.caseName || `Case ${item.caseId}`}</span></button>)}
            {overdue.length === 0 && <p className="helper-text">No assistant work is overdue.</p>}
          </div>
        </section>

        <section className="ui-table-panel">
          <div className="panel-hd"><h3>Service and Publication</h3><span className="count">{service.length}</span></div>
          <div className="assistant-list">
            {service.slice(0, 8).map((item) => <button className="assistant-list-row" key={item.id} onClick={() => onOpenCase(item.id)}><span><strong>{item.caseName}</strong><small>{item.county || 'County not set'}</small></span><span><strong>Service pending</strong><small>{item.serviceDeadline120 ? `Review by ${dateLabel(item.serviceDeadline120)}` : 'Review date not set'}</small></span></button>)}
            {service.length === 0 && <p className="helper-text">No service follow-up is currently due.</p>}
          </div>
        </section>
      </div>
    </main>
  )
}
