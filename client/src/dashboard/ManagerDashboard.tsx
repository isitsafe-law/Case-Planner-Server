import { useEffect, useMemo, useState } from 'react'
import type { CaseRecord, DeadlineItem, Hearing } from '../App'
import { Panel } from '../App'
import { Btn } from '../ui/Btn'
import { downloadCsv } from '../ui/csvExport'
import type { PreFilingMilestoneAgingSummary, PreFilingMilestoneRecord, ReminderRequestRecord, ReviewNoteRecord } from './types'
import { ManagerCalendarTab, type CalendarHorizon } from './ManagerCalendarTab'
import { DivisionPipelineTab } from './DivisionPipelineTab'
import { ByAttorneyTab } from './ByAttorneyTab'
import { NeedsAttentionTab } from './NeedsAttentionTab'
import { DATA_QUALITY_AREAS, METRIC_DEFINITIONS, type DataQualityReport } from './dataQuality'
import { api } from '../App'
import { ManagerDashboardVisuals, buildManagerHardDateBars, buildManagerTrialBars } from './ManagerDashboardVisuals'
import { buildNeedsAttentionRows } from './NeedsAttentionTab'
import { holderDistribution } from './FilingPipelinePanel'

// Deliberately a narrow local shape rather than importing App.tsx's full ChecklistItem - same
// precedent as LegalAssistantDashboard.tsx's AssistantWork/EventPreparationPage.tsx's
// PreparationWork - only checklist tasks carry ownerRole (deadlines don't), so "assistant-owned
// work" is checklist-only per that same established convention.
type AssistantOwnedWorkItem = {
  id: number
  caseId: number
  relatedEventId?: number | null
  task?: string
  dueDate?: string | null
  status?: string | null
  assignedStaffName?: string | null
  ownerRole?: string
}

// Mirrors server ServiceQueueItem - the real ServiceStatusEngine output, replacing the ad hoc
// raw-filingDate-age rule this dashboard used to compute serviceRiskCount with directly.
type ManagerServiceQueueItem = { caseId: number; warningLevel: string }

const openWorkStatus = (value?: string | null) => !['Done', 'Complete', 'Completed', 'N/A'].includes(value || '')
const isOverdue = (dueDate?: string | null) => Boolean(dueDate) && new Date(`${dueDate!.slice(0, 10)}T00:00:00`) < new Date(new Date().toDateString())
const SERVICE_RISK_WARNING_LEVELS = new Set(['missing', 'overdue', 'urgent', 'high', 'warning'])

type ManagerDashboardTab = 'calendar' | 'pipeline' | 'byAttorney' | 'needsAttention'

const MANAGER_DASHBOARD_TABS: { key: ManagerDashboardTab; label: string }[] = [
  { key: 'calendar', label: 'Calendar' },
  { key: 'pipeline', label: 'Pipeline' },
  { key: 'byAttorney', label: 'By Attorney' },
  { key: 'needsAttention', label: 'Needs Attention' },
]

// Dashboard-context "open" definition - excludes Triage as well as Resolved / Closed. Mirrors
// App.tsx's upcomingWorkItems `eligible()` check / docketCases's 'filed' branch exactly (both
// search "Resolved / Closed" for this precedent), which is the dashboard-specific variant of
// "open" elsewhere in this file - distinct from the exported isOpenCase, which is Report A's own
// variant and deliberately does NOT exclude Triage (see its doc comment in App.tsx).
// Exported so ByAttorneyTab/NeedsAttentionTab (Milestone 5, part 2) can reuse this exact definition
// rather than each writing a second/third variant of "what counts as open" division-wide.
export function isOpenForDivision(record: CaseRecord): boolean {
  const caseStatus = record.caseStatus || 'Pipeline'
  return caseStatus !== 'Resolved / Closed' && caseStatus !== 'Triage' && record.status !== 'Closed' && record.status !== 'Triage'
}

// Same "needs attention" fields the Attorney Dashboard already renders (StatusChip off
// attentionStatus, the "No answer on file" warning off defaultPostureWarning) - an interim
// division-wide count. Exported for ByAttorneyTab's "needs-attention count" column (Milestone 5,
// part 2); the Needs Attention tab itself builds a separate, richer, rule-based exception list on
// top of this rather than replacing it - see NeedsAttentionTab.tsx.
export function needsAttention(record: CaseRecord): boolean {
  return (record.attentionStatus || 'onTrack') !== 'onTrack' || record.defaultPostureWarning === true
}

export function ManagerDashboard({
  allCases,
  hearings,
  deadlines,
  checklist,
  serviceQueue,
  openReminders,
  preFilingMilestones,
  preFilingMilestonesAging,
  reviewNotes,
  pendingEventChangeIds,
  onOpenCase,
}: {
  allCases: CaseRecord[]
  hearings: Hearing[]
  deadlines: DeadlineItem[]
  checklist: AssistantOwnedWorkItem[]
  serviceQueue: ManagerServiceQueueItem[]
  openReminders: ReminderRequestRecord[]
  preFilingMilestones: PreFilingMilestoneRecord[]
  preFilingMilestonesAging: PreFilingMilestoneAgingSummary | null
  reviewNotes: ReviewNoteRecord[]
  pendingEventChangeIds: Set<number>
  onOpenCase: (caseId: number) => void
}) {
  const [activeTab, setActiveTab] = useState<ManagerDashboardTab>('calendar')
  const [horizon, setHorizon] = useState<CalendarHorizon>(30)
  const [calendarMinimumDays, setCalendarMinimumDays] = useState(0)
  const [calendarEventType, setCalendarEventType] = useState('All')
  const [calendarAttorney, setCalendarAttorney] = useState('All')
  const [dataQuality, setDataQuality] = useState<DataQualityReport | null>(null)
  const [qualityArea, setQualityArea] = useState<string>('All')
  const [qualitySeverity, setQualitySeverity] = useState<string>('All')

  useEffect(() => {
    void api<DataQualityReport>('/api/data-quality').then(setDataQuality).catch(() => setDataQuality(null))
  }, [])

  const managementAttentionRows = useMemo(() => buildNeedsAttentionRows(allCases, preFilingMilestones, reviewNotes, 14, 60), [allCases, preFilingMilestones, reviewNotes])
  const needsAttentionCount = managementAttentionRows.length
  const openCases = useMemo(() => allCases.filter(isOpenForDivision), [allCases])
  const totalOpenCount = openCases.length
  const openPipelineCount = useMemo(() => openCases.filter((record) => (record.caseStatus || 'Pipeline') === 'Pipeline').length, [openCases])
  const openFiledCount = totalOpenCount - openPipelineCount
  const openNeedsAttentionCount = useMemo(() => openCases.filter(needsAttention).length, [openCases])
  const caseById = useMemo(() => new Map(allCases.map((record) => [record.id, record])), [allCases])
  const qualityIssues = useMemo(() => (dataQuality?.issues ?? []).filter((issue) => issue.count > 0 && (qualityArea === 'All' || issue.area === qualityArea) && (qualitySeverity === 'All' || issue.severity === qualitySeverity)), [dataQuality, qualityArea, qualitySeverity])
  const qualityFindingCount = useMemo(() => (dataQuality?.issues ?? []).filter((issue) => issue.count > 0).reduce((sum, issue) => sum + issue.count, 0), [dataQuality])
  const hardDateBars = useMemo(() => buildManagerHardDateBars(allCases, hearings, deadlines), [allCases, hearings, deadlines])
  const trialBars = useMemo(() => buildManagerTrialBars(allCases, hearings), [allCases, hearings])
  const hardDateCount = hardDateBars.slice(0, 3).reduce((sum, bar) => sum + bar.count, 0)
  const juryTrialCount = trialBars.reduce((sum, bar) => sum + bar.count, 0)
  const pipelineStallCount = useMemo(() => (preFilingMilestonesAging?.cases ?? []).filter((row) => (row.daysSinceMarked ?? 0) > 60).length, [preFilingMilestonesAging])
  // Replaces a former ad hoc rule keyed on raw filingDate age (ignored manual ServiceDeadline120
  // overrides and ServiceDeadlineBasisDate, and duplicated its own 90-day threshold client-side).
  // Now reuses the same ServiceStatusEngine bands already computed server-side and already used by
  // the Legal Assistant Dashboard's own Service and Publication section - one shared definition of
  // "at risk," not three (this dashboard, the assistant dashboard, and the engine) drifting apart.
  const serviceRiskCount = useMemo(() => serviceQueue.filter((item) => SERVICE_RISK_WARNING_LEVELS.has(item.warningLevel)).length, [serviceQueue])
  const pendingEventChanges = useMemo(() => hearings.filter((event) => pendingEventChangeIds.has(event.id)), [hearings, pendingEventChangeIds])

  // Legal Assistant Dashboard audit Phase 6 ("Manager additions"): compact assistant risk/coverage
  // context, not a completion leaderboard - see each count's comment for what it represents.
  const assistantWork = useMemo(() => checklist.filter((item) => item.ownerRole === 'LegalAssistant' && openWorkStatus(item.status)), [checklist])
  const assistantOverdueWork = useMemo(() => assistantWork.filter((item) => isOverdue(item.dueDate)), [assistantWork])
  // "Temporary coverage/unassigned work" - assistant-scoped tasks with nobody named as the owner,
  // a staffing gap rather than a normal backlog item.
  const unassignedAssistantWork = useMemo(() => assistantWork.filter((item) => !item.assignedStaffName), [assistantWork])
  // "Event-preparation risks with an actual overdue/open condition" - linked prep work (checklist
  // or deadline items carrying a RelatedEventId) that's actually overdue, not every open item tied
  // to an event.
  const eventPrepRiskCount = useMemo(
    () => [...checklist, ...deadlines].filter((item) => item.relatedEventId != null && openWorkStatus(item.status) && isOverdue(item.dueDate)).length,
    [checklist, deadlines],
  )
  const holderPipelineDistribution = useMemo(
    () => holderDistribution(allCases.filter((record) => (record.caseStatus || 'Pipeline') === 'Pipeline')),
    [allCases],
  )

  function exportDataQuality() {
    if (!dataQuality) return
    downloadCsv(`Division_Data_Quality_${new Date().toISOString().slice(0, 10)}.csv`, dataQuality.issues.map((issue) => ({
      Check: issue.label,
      Area: issue.area,
      Severity: issue.severity,
      Count: issue.count,
      Definition: issue.definition,
      SuggestedAction: issue.suggestedAction,
      SampleCaseIds: issue.sampleCaseIds.join('; '),
      AdditionalCaseCount: issue.additionalCaseCount,
    })))
  }

  function goToCalendar(nextHorizon?: CalendarHorizon, eventType = 'All', attorney = 'All', minimumDays = 0) {
    setActiveTab('calendar')
    if (nextHorizon) setHorizon(nextHorizon)
    setCalendarEventType(eventType)
    setCalendarAttorney(attorney)
    setCalendarMinimumDays(minimumDays)
  }

  return (
    <main className="page">
      <div className="dash-hd">
        <h2>Division Overview</h2>
        <span className="dash-date">Testing view pending Microsoft Entra ID integration.</span>
      </div>

      <details className="metric-definition-disclosure">
        <summary>Metric definitions and data quality</summary>
        <p className="helper-text">These definitions are the current management-view contract. Counts are computed from the active SQLite case and event data.</p>
        <div className="metric-definition-list">
          {METRIC_DEFINITIONS.map(([label, definition]) => <div key={label}><strong>{label}</strong><span>{definition}</span></div>)}
        </div>
        {dataQuality && (
          <div className="top-gap-small">
            <div className="button-row compact-actions"><strong>Data-quality checks</strong><span className="helper-text">{qualityFindingCount} affected records across {(dataQuality?.issues ?? []).filter((issue) => issue.count > 0).length} findings</span><button onClick={exportDataQuality} disabled={!dataQuality}>Export CSV</button></div>
            <div className="button-row compact-actions top-gap-small"><label>Area <select value={qualityArea} onChange={(event) => setQualityArea(event.target.value)}><option>All</option>{DATA_QUALITY_AREAS.map((area) => <option key={area}>{area}</option>)}</select></label><label>Severity <select value={qualitySeverity} onChange={(event) => setQualitySeverity(event.target.value)}><option>All</option><option>Critical</option><option>Warning</option><option>Info</option></select></label></div>
            {qualityIssues.length === 0 ? <p className="helper-text">No current findings match these filters.</p> : <div className="table-wrap top-gap-small"><table className="ui-table compact-table"><thead><tr><th>Area</th><th>Check</th><th>Severity</th><th>Count</th><th>Suggested action</th></tr></thead><tbody>{qualityIssues.map((issue) => <tr key={issue.key}><td>{issue.area}</td><td><strong>{issue.label}</strong><div className="ui-sub">{issue.definition}</div></td><td>{issue.severity}</td><td className={`ui-data${issue.severity === 'Critical' ? ' ui-cell-danger' : ' ui-cell-warn'}`}>{issue.count}</td><td><div>{issue.suggestedAction}</div>{issue.sampleCaseIds.length > 0 && <div className="data-quality-case-links">{issue.sampleCaseIds.slice(0, 3).map((caseId) => { const record = caseById.get(caseId); return <Btn key={caseId} size="sm" onClick={() => onOpenCase(caseId)}>Open {record?.caseNumber || record?.caseName || `Case ${caseId}`}</Btn> })}{issue.additionalCaseCount > 0 && <span className="helper-text">+{issue.additionalCaseCount} more affected</span>}</div>}</td></tr>)}</tbody></table></div>}
          </div>
        )}
      </details>

      <ManagerDashboardVisuals
        attentionCount={needsAttentionCount}
        hardDateCount={hardDateCount}
        trialCount={juryTrialCount}
        pipelineStallCount={pipelineStallCount}
        serviceRiskCount={serviceRiskCount}
        onAttention={() => setActiveTab('needsAttention')}
        onHardDates={() => goToCalendar(90)}
        onTrials={() => goToCalendar(180, 'Jury Trial')}
        onPipeline={() => setActiveTab('pipeline')}
        onService={() => setActiveTab('needsAttention')}
      />
      <div className="helper-text management-workload-summary" aria-label="Open tract workload summary">
        {openPipelineCount} pipeline · {openFiledCount} filed · {openNeedsAttentionCount} need attention
      </div>

      {pendingEventChanges.length > 0 && <section className="ui-table-panel pending-manager-approvals"><div className="panel-hd"><h3>Pending event-date approvals</h3><span className="count">{pendingEventChanges.length}</span></div><div className="assistant-list">{pendingEventChanges.slice(0, 6).map((event) => <button className="assistant-list-row" key={event.id} onClick={() => onOpenCase(event.caseId)}><span><strong>{event.title || event.eventType || 'Proceeding'}</strong><small>{event.hearingDate || 'Date not set'}</small></span><span>Review proposed date change</span></button>)}</div></section>}

      {/* Legal Assistant Dashboard audit Phase 6: risk/coverage context, not a completion
          leaderboard - counts and exception lists only, no per-assistant activity ranking. */}
      <section className="ui-table-panel">
        <div className="panel-hd"><h3>Legal Assistant Coverage</h3></div>
        <div className="ui-tiles dashboard-kpi-strip">
          <div className="metric-tile"><span className="metric-label">Waiting on Attorney</span><strong>{openReminders.length}</strong><small>Open assistant reminder requests</small></div>
          <div className="metric-tile"><span className="metric-label">Assistant Work</span><strong>{assistantWork.length}</strong><small>{assistantOverdueWork.length} overdue</small></div>
          <div className="metric-tile"><span className="metric-label">Unassigned Work</span><strong>{unassignedAssistantWork.length}</strong><small>Assistant-scoped, no owner named</small></div>
          <div className="metric-tile"><span className="metric-label">Event Prep Risk</span><strong>{eventPrepRiskCount}</strong><small>Linked prep work overdue</small></div>
        </div>
        <div className="pipeline-holder-summary top-gap-small">
          {holderPipelineDistribution.map(({ holder, count }) => (
            <div key={holder} className="pipeline-holder-summary-item">
              <span>{holder}</span>
              <strong>{count}</strong>
            </div>
          ))}
        </div>
        {openReminders.length > 0 && <div className="assistant-list top-gap-small">
          <p className="eyebrow">Waiting on attorney</p>
          {openReminders.slice(0, 6).map((reminder) => <button className="assistant-list-row" key={reminder.id} onClick={() => onOpenCase(reminder.caseId)}><span><strong>{reminder.requestedAction || 'Follow-up requested'}</strong><small>{reminder.targetAttorneyDisplay || 'Attorney not set'}</small></span><span>{reminder.followUpDate ? `Follow up ${reminder.followUpDate}` : 'No follow-up date'}</span></button>)}
        </div>}
        {unassignedAssistantWork.length > 0 && <div className="assistant-list top-gap-small">
          <p className="eyebrow">Unassigned assistant work</p>
          {unassignedAssistantWork.slice(0, 6).map((item) => <button className="assistant-list-row" key={item.id} onClick={() => onOpenCase(item.caseId)}><span><strong>{item.task || 'Task'}</strong><small>{item.dueDate ? `Due ${item.dueDate}` : 'No due date'}</small></span><span>No owner named</span></button>)}
        </div>}
      </section>
      <div className="segmented-tabs">
        {MANAGER_DASHBOARD_TABS.map((tab) => (
          <button key={tab.key} className={tab.key === activeTab ? 'segment active' : 'segment'} onClick={() => setActiveTab(tab.key)}>
            {tab.label}
          </button>
        ))}
      </div>

      <div className="top-gap-small">
        {activeTab === 'calendar' && (
          <div className="mgr-calendar-grid">
            <Panel title="Chronological Events">
              <ManagerCalendarTab
                allCases={allCases}
                hearings={hearings}
                horizon={horizon}
                onHorizonChange={setHorizon}
                onOpenCase={onOpenCase}
                initialEventType={calendarEventType}
                initialAttorney={calendarAttorney}
                minimumDays={calendarMinimumDays}
              />
            </Panel>
          </div>
        )}

        {activeTab === 'pipeline' && (
          <Panel title="Division Pipeline">
            <DivisionPipelineTab allCases={allCases} preFilingMilestones={preFilingMilestones} preFilingMilestonesAging={preFilingMilestonesAging} reviewNotes={reviewNotes} onOpenCase={onOpenCase} />
          </Panel>
        )}

        {activeTab === 'byAttorney' && (
          <Panel title="By Attorney">
            <ByAttorneyTab allCases={allCases} hearings={hearings} deadlines={deadlines} onOpenCase={onOpenCase} />
          </Panel>
        )}
        {activeTab === 'needsAttention' && (
          <Panel title="Needs Attention">
            <NeedsAttentionTab
              allCases={allCases}
              preFilingMilestones={preFilingMilestones}
              reviewNotes={reviewNotes}
              onOpenCase={onOpenCase}
            />
          </Panel>
        )}
      </div>
    </main>
  )
}
