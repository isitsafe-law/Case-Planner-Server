import { useEffect, useMemo, useState } from 'react'
import type { CaseRecord, DeadlineItem, Hearing } from '../App'
import { Panel } from '../App'
import { MetricTile } from '../ui/MetricTile'
import { Btn } from '../ui/Btn'
import { downloadCsv } from '../ui/csvExport'
import type { PreFilingMilestoneAgingSummary, PreFilingMilestoneRecord, ReviewNoteRecord } from './types'
import { ManagerCalendarTab, countEventsInWindow, type CalendarHorizon } from './ManagerCalendarTab'
import { DivisionPipelineTab } from './DivisionPipelineTab'
import { ByAttorneyTab } from './ByAttorneyTab'
import { NeedsAttentionTab } from './NeedsAttentionTab'
import { METRIC_DEFINITIONS, type DataQualityReport } from './dataQuality'
import { api } from '../App'

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
  preFilingMilestones,
  preFilingMilestonesAging,
  reviewNotes,
  onOpenCase,
}: {
  allCases: CaseRecord[]
  hearings: Hearing[]
  deadlines: DeadlineItem[]
  preFilingMilestones: PreFilingMilestoneRecord[]
  preFilingMilestonesAging: PreFilingMilestoneAgingSummary | null
  reviewNotes: ReviewNoteRecord[]
  onOpenCase: (caseId: number) => void
}) {
  const [activeTab, setActiveTab] = useState<ManagerDashboardTab>('calendar')
  const [horizon, setHorizon] = useState<CalendarHorizon>(30)
  const [dataQuality, setDataQuality] = useState<DataQualityReport | null>(null)

  useEffect(() => {
    void api<DataQualityReport>('/api/data-quality').then(setDataQuality).catch(() => setDataQuality(null))
  }, [])

  const eventsNext7 = useMemo(() => countEventsInWindow(allCases, hearings, 7), [allCases, hearings])
  const eventsNext30 = useMemo(() => countEventsInWindow(allCases, hearings, 30), [allCases, hearings])
  const needsAttentionCount = useMemo(() => allCases.filter(needsAttention).length, [allCases])
  const pipelineCount = useMemo(() => allCases.filter((record) => (record.caseStatus || 'Pipeline') === 'Pipeline').length, [allCases])
  const openCases = useMemo(() => allCases.filter(isOpenForDivision), [allCases])
  const totalOpenCount = openCases.length
  const openPipelineCount = useMemo(() => openCases.filter((record) => (record.caseStatus || 'Pipeline') === 'Pipeline').length, [openCases])
  const openFiledCount = totalOpenCount - openPipelineCount
  const openNeedsAttentionCount = useMemo(() => openCases.filter(needsAttention).length, [openCases])
  const caseById = useMemo(() => new Map(allCases.map((record) => [record.id, record])), [allCases])

  function exportDataQuality() {
    if (!dataQuality) return
    downloadCsv(`Division_Data_Quality_${new Date().toISOString().slice(0, 10)}.csv`, dataQuality.issues.map((issue) => ({
      Check: issue.label,
      Severity: issue.severity,
      Count: issue.count,
      Definition: issue.definition,
      SuggestedAction: issue.suggestedAction,
      SampleCaseIds: issue.sampleCaseIds.join('; '),
    })))
  }

  function goToCalendar(nextHorizon?: CalendarHorizon) {
    setActiveTab('calendar')
    if (nextHorizon) setHorizon(nextHorizon)
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
            <div className="button-row compact-actions"><strong>Data-quality checks</strong><button onClick={exportDataQuality} disabled={!dataQuality}>Export CSV</button></div>
            {dataQuality.issues.filter((issue) => issue.count > 0).length === 0 ? <p className="helper-text">No current issues detected.</p> : <div className="table-wrap top-gap-small"><table className="ui-table compact-table"><thead><tr><th>Check</th><th>Count</th><th>Suggested action</th></tr></thead><tbody>{dataQuality.issues.filter((issue) => issue.count > 0).map((issue) => <tr key={issue.key}><td><strong>{issue.label}</strong><div className="ui-sub">{issue.definition}</div></td><td className={`ui-data${issue.severity === 'Critical' ? ' ui-cell-danger' : ' ui-cell-warn'}`}>{issue.count}</td><td><div>{issue.suggestedAction}</div>{issue.sampleCaseIds.length > 0 && <div className="data-quality-case-links">{issue.sampleCaseIds.slice(0, 3).map((caseId) => { const record = caseById.get(caseId); return <Btn key={caseId} size="sm" onClick={() => onOpenCase(caseId)}>Open {record?.caseNumber || record?.caseName || `Case ${caseId}`}</Btn> })}{issue.sampleCaseIds.length > 3 && <span className="helper-text">+{issue.sampleCaseIds.length - 3} more</span>}</div>}</td></tr>)}</tbody></table></div>}
          </div>
        )}
      </details>

      <div className="ui-tiles" style={{ marginBottom: '1rem', flexWrap: 'wrap' }}>
        <MetricTile
          label="Events next 7 days"
          value={eventsNext7}
          active={activeTab === 'calendar' && horizon === 7}
          onClick={() => goToCalendar(7)}
        />
        <MetricTile
          label="Events next 30 days"
          value={eventsNext30}
          active={activeTab === 'calendar' && horizon === 30}
          onClick={() => goToCalendar(30)}
        />
        <MetricTile
          label="Needs-attention count"
          value={needsAttentionCount}
          tone={needsAttentionCount > 0 ? 'danger' : 'default'}
          active={activeTab === 'needsAttention'}
          onClick={() => setActiveTab('needsAttention')}
        />
        <MetricTile label="Tracts in Pipeline" value={pipelineCount} active={activeTab === 'pipeline'} onClick={() => setActiveTab('pipeline')} />
        <MetricTile label="Open tracts · division" value={totalOpenCount} />
      </div>
      <div className="helper-text management-workload-summary" aria-label="Open tract workload summary">
        {openPipelineCount} pipeline · {openFiledCount} filed · {openNeedsAttentionCount} need attention
      </div>

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
