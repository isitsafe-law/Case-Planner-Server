import type { CaseRecord } from '../App'
import { Panel } from '../App'
import { FilingStatusSection } from './FilingStatusSection'
import { SettlementAuthoritySection } from './SettlementAuthoritySection'
import type { PreFilingMilestoneAgingSummary, SettlementAuthorityRequestRecord } from './types'

// Manager/Administrator Dashboard Milestone 5, part 1 of 2. The Approvals tab.
//
// Milestone 4's correction retired the in-system "Filing Approval" decision entirely - ARDOT's real
// pre-filing sign-off happens by email, outside this system (see PreFilingMilestoneRecord's doc
// comment in server/CasePlanner.Web.Server/Models/DomainModels.cs for the full history). So this
// tab is NOT the original spec's single two-workflow approval queue; it's two different sections:
//   (a) Settlement Authority - a sortable log of requests and their recorded outcomes (Manager
//       Dashboard sign-off consolidation, item 4 made this pure record-keeping, open to anyone with
//       case-write access - no longer a Chief-Counsel-only decision queue).
//   (b) Filing Status - a read-only informational list of which pre-filing milestone every Pipeline
//       tract is waiting on. No approve/deny buttons - marking a milestone records a fact, not a
//       decision, and is already reachable from the case workspace by anyone with the right access.
//       A later milestone, not this one, decides whether to also expose mark/unmark actions here.
export function ApprovalsTab({
  allCases,
  settlementAuthorityRequests,
  preFilingMilestonesAging,
  onOpenCase,
  onDecided,
}: {
  allCases: CaseRecord[]
  settlementAuthorityRequests: SettlementAuthorityRequestRecord[]
  preFilingMilestonesAging: PreFilingMilestoneAgingSummary | null
  onOpenCase: (caseId: number) => void
  onDecided: () => Promise<void>
}) {
  return (
    <div>
      <Panel title="Settlement Authority">
        <SettlementAuthoritySection
          allCases={allCases}
          settlementAuthorityRequests={settlementAuthorityRequests}
          onOpenCase={onOpenCase}
          onDecided={onDecided}
        />
      </Panel>
      <Panel title="Filing Status" className="top-gap-small">
        <FilingStatusSection aging={preFilingMilestonesAging} onOpenCase={onOpenCase} />
      </Panel>
    </div>
  )
}
