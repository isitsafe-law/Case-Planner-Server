import { useMemo, useState } from 'react'
import type { CaseRecord, Hearing } from '../App'
import { Panel } from '../App'
import { MetricTile } from '../ui/MetricTile'
import type { PreFilingMilestoneAgingSummary, PreFilingMilestoneRecord, ReviewNoteRecord, SettlementAuthorityRequestRecord } from './types'
import { ManagerCalendarTab, countEventsInWindow, type CalendarHorizon } from './ManagerCalendarTab'
import { IncomingPipelinePanel } from './IncomingPipelinePanel'
import { ApprovalsTab } from './ApprovalsTab'
import { ByAttorneyTab } from './ByAttorneyTab'
import { ByJobTab } from './ByJobTab'
import { NeedsAttentionTab } from './NeedsAttentionTab'
import { BulkMilestoneGrid } from './BulkMilestoneGrid'

type ManagerDashboardTab = 'calendar' | 'approvals' | 'byAttorney' | 'byJob' | 'needsAttention' | 'bulkMark'

const MANAGER_DASHBOARD_TABS: { key: ManagerDashboardTab; label: string }[] = [
  { key: 'calendar', label: 'Calendar' },
  { key: 'approvals', label: 'Approvals' },
  { key: 'byAttorney', label: 'By Attorney' },
  { key: 'byJob', label: 'By Job' },
  { key: 'needsAttention', label: 'Needs Attention' },
  { key: 'bulkMark', label: 'Bulk Mark Milestones' },
]

// Dashboard-context "open" definition - excludes Triage as well as Resolved / Closed. Mirrors
// App.tsx's upcomingWorkItems `eligible()` check / docketCases's 'filed' branch exactly (both
// search "Resolved / Closed" for this precedent), which is the dashboard-specific variant of
// "open" elsewhere in this file - distinct from the exported isOpenCase, which is Report A's own
// variant and deliberately does NOT exclude Triage (see its doc comment in App.tsx).
// Exported so ByAttorneyTab/ByJobTab/NeedsAttentionTab (Milestone 5, part 2) can reuse this exact
// definition rather than each writing a third/fourth variant of "what counts as open" division-wide.
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
  settlementAuthorityRequests,
  preFilingMilestones,
  preFilingMilestonesAging,
  reviewNotes,
  onOpenCase,
  onDecided,
  onMilestonesMutated,
}: {
  allCases: CaseRecord[]
  hearings: Hearing[]
  settlementAuthorityRequests: SettlementAuthorityRequestRecord[]
  preFilingMilestones: PreFilingMilestoneRecord[]
  preFilingMilestonesAging: PreFilingMilestoneAgingSummary | null
  reviewNotes: ReviewNoteRecord[]
  onOpenCase: (caseId: number) => void
  // Manager/Administrator Dashboard Milestone 5: re-fetches settlementAuthorityRequests after a
  // successful Settlement Authority decide action - see App.tsx's refreshSettlementAuthorityRequests.
  onDecided: () => Promise<void>
  // Final implementation, item 1: re-fetches preFilingMilestones/preFilingMilestonesAging after a
  // bulk-mark action - see App.tsx's refreshPreFilingMilestones.
  onMilestonesMutated: () => Promise<void>
}) {
  const [activeTab, setActiveTab] = useState<ManagerDashboardTab>('calendar')
  const [horizon, setHorizon] = useState<CalendarHorizon>(30)

  const eventsNext7 = useMemo(() => countEventsInWindow(allCases, hearings, 7), [allCases, hearings])
  const eventsNext30 = useMemo(() => countEventsInWindow(allCases, hearings, 30), [allCases, hearings])
  const awaitingApprovalCount = useMemo(
    () => settlementAuthorityRequests.filter((request) => request.status === 'Pending').length,
    [settlementAuthorityRequests],
  )
  const needsAttentionCount = useMemo(() => allCases.filter(needsAttention).length, [allCases])
  const pipelineCount = useMemo(() => allCases.filter((record) => (record.caseStatus || 'Pipeline') === 'Pipeline').length, [allCases])
  const totalOpenCount = useMemo(() => allCases.filter(isOpenForDivision).length, [allCases])

  function goToCalendar(nextHorizon?: CalendarHorizon) {
    setActiveTab('calendar')
    if (nextHorizon) setHorizon(nextHorizon)
  }

  return (
    <main className="page">
      <div className="dash-hd">
        <h2>Division Overview</h2>
        <span className="dash-date">A 30,000-foot view across every tract in the division.</span>
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
          label="Awaiting my approval"
          value={awaitingApprovalCount}
          tone={awaitingApprovalCount > 0 ? 'warn' : 'default'}
          active={activeTab === 'approvals'}
          onClick={() => setActiveTab('approvals')}
        />
        <MetricTile
          label="Needs-attention count"
          value={needsAttentionCount}
          tone={needsAttentionCount > 0 ? 'danger' : 'default'}
          active={activeTab === 'needsAttention'}
          onClick={() => setActiveTab('needsAttention')}
        />
        <MetricTile label="Tracts in Pipeline" value={pipelineCount} onClick={() => goToCalendar()} />
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
            <Panel title="Incoming Pipeline">
              <IncomingPipelinePanel allCases={allCases} preFilingMilestones={preFilingMilestones} reviewNotes={reviewNotes} onOpenCase={onOpenCase} onMutated={onMilestonesMutated} />
            </Panel>
          </div>
        )}

        {activeTab === 'approvals' && (
          <ApprovalsTab
            allCases={allCases}
            settlementAuthorityRequests={settlementAuthorityRequests}
            preFilingMilestonesAging={preFilingMilestonesAging}
            onOpenCase={onOpenCase}
            onDecided={onDecided}
          />
        )}
        {activeTab === 'byAttorney' && (
          <Panel title="By Attorney">
            <ByAttorneyTab allCases={allCases} hearings={hearings} settlementAuthorityRequests={settlementAuthorityRequests} onOpenCase={onOpenCase} />
          </Panel>
        )}
        {activeTab === 'byJob' && (
          <Panel title="By Job">
            <ByJobTab allCases={allCases} hearings={hearings} onOpenCase={onOpenCase} />
          </Panel>
        )}
        {activeTab === 'needsAttention' && (
          <Panel title="Needs Attention">
            <NeedsAttentionTab
              allCases={allCases}
              settlementAuthorityRequests={settlementAuthorityRequests}
              preFilingMilestones={preFilingMilestones}
              reviewNotes={reviewNotes}
              onOpenCase={onOpenCase}
            />
          </Panel>
        )}
        {activeTab === 'bulkMark' && (
          <Panel title="Bulk Mark Milestones">
            <BulkMilestoneGrid allCases={allCases} preFilingMilestones={preFilingMilestones} onMutated={onMilestonesMutated} />
          </Panel>
        )}
      </div>
    </main>
  )
}
