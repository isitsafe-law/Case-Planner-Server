import { useMemo, useState } from 'react'
import type { CaseRecord, Hearing } from '../App'
import { Panel } from '../App'
import { MetricTile } from '../ui/MetricTile'
import type { PreFilingMilestoneAgingSummary, PreFilingMilestoneRecord, ReviewNoteRecord } from './types'
import { ManagerCalendarTab, countEventsInWindow, type CalendarHorizon } from './ManagerCalendarTab'
import { DivisionPipelineTab } from './DivisionPipelineTab'
import { FilingStatusSection } from './FilingStatusSection'
import { ByAttorneyTab } from './ByAttorneyTab'
import { NeedsAttentionTab } from './NeedsAttentionTab'

type ManagerDashboardTab = 'calendar' | 'pipeline' | 'filingStatus' | 'byAttorney' | 'needsAttention'

const MANAGER_DASHBOARD_TABS: { key: ManagerDashboardTab; label: string }[] = [
  { key: 'calendar', label: 'Calendar' },
  { key: 'pipeline', label: 'Pipeline' },
  { key: 'filingStatus', label: 'Filing Status' },
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
  preFilingMilestones,
  preFilingMilestonesAging,
  reviewNotes,
  onOpenCase,
}: {
  allCases: CaseRecord[]
  hearings: Hearing[]
  preFilingMilestones: PreFilingMilestoneRecord[]
  preFilingMilestonesAging: PreFilingMilestoneAgingSummary | null
  reviewNotes: ReviewNoteRecord[]
  onOpenCase: (caseId: number) => void
}) {
  const [activeTab, setActiveTab] = useState<ManagerDashboardTab>('calendar')
  const [horizon, setHorizon] = useState<CalendarHorizon>(30)

  const eventsNext7 = useMemo(() => countEventsInWindow(allCases, hearings, 7), [allCases, hearings])
  const eventsNext30 = useMemo(() => countEventsInWindow(allCases, hearings, 30), [allCases, hearings])
  const needsAttentionCount = useMemo(() => allCases.filter(needsAttention).length, [allCases])
  const pipelineCount = useMemo(() => allCases.filter((record) => (record.caseStatus || 'Pipeline') === 'Pipeline').length, [allCases])
  const unassignedPipelineCount = useMemo(() => allCases.filter((record) => (record.caseStatus || 'Pipeline') === 'Pipeline' && !record.assignedAttorney).length, [allCases])
  const totalOpenCount = useMemo(() => allCases.filter(isOpenForDivision).length, [allCases])

  function goToCalendar(nextHorizon?: CalendarHorizon) {
    setActiveTab('calendar')
    if (nextHorizon) setHorizon(nextHorizon)
  }

  return (
    <main className="page">
      <div className="dash-hd">
        <h2>Division Overview</h2>
        <span className="dash-date">A 30,000-foot view across every tract in the division. Local SQLite preview; manager-only enforcement begins with Entra.</span>
      </div>

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
        <MetricTile label="Unassigned Pipeline" value={unassignedPipelineCount} tone={unassignedPipelineCount > 0 ? 'warn' : 'default'} active={activeTab === 'pipeline'} onClick={() => setActiveTab('pipeline')} />
        <MetricTile label="Total open tracts" value={totalOpenCount} onClick={() => goToCalendar()} />
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
            <DivisionPipelineTab allCases={allCases} preFilingMilestones={preFilingMilestones} reviewNotes={reviewNotes} onOpenCase={onOpenCase} />
          </Panel>
        )}

        {activeTab === 'filingStatus' && (
          <Panel title="Filing Status">
            <FilingStatusSection aging={preFilingMilestonesAging} onOpenCase={onOpenCase} />
          </Panel>
        )}
        {activeTab === 'byAttorney' && (
          <Panel title="By Attorney">
            <ByAttorneyTab allCases={allCases} hearings={hearings} onOpenCase={onOpenCase} />
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
