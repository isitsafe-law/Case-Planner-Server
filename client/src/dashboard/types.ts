// Mirrors server/CasePlanner.Web.Server/Models/DomainModels.cs's AttorneyDashboardResponse and
// sub-shapes exactly (field-for-field) - see GetAttorneyDashboardAsync / AttorneyDashboardEngine.

export type ActionCategory = 'Decide' | 'Act' | 'Review' | 'Escalate' | 'Prepare'
export type MomentumStatus = 'Moving' | 'Waiting Appropriately' | 'Review Required' | 'Stalled'
export type MatterType = 'PreFilingTract' | 'FiledCase'
export type MatterPriority = 'Normal' | 'Priority' | 'Rushed'
export type CurrentHolder = 'Legal Assistant' | 'Attorney' | 'Deputy Chief Counsel' | 'Chief Counsel' | 'Filing Staff' | 'Other'
export type PipelineStage =
  | 'With Legal Assistant'
  | 'With Attorney'
  | 'With Deputy Chief Counsel'
  | 'With Chief Counsel'
  | 'Returned for Revision'
  | 'Approved for Filing'
  | 'Filed'
export type DiscoveryStrategy =
  | 'No discovery currently needed'
  | 'Written discovery first'
  | 'Landowner deposition first'
  | 'Appraiser discovery first'
  | 'Limited targeted discovery'
  | 'Full discovery plan'
  | 'Awaiting owner appraisal before deciding'
  | 'Strategy deferred until a stated event'
  | 'Strategy not selected'

export type AttorneyDashboardSummaryCounts = {
  needsJudgment: number
  stalled: number
  discoveryUnset: number
  onMyDesk: number
  trialTrack: number
  missingNextReview: number
}

export type ActionQueueItem = {
  caseId: number
  caseName: string
  caseNumber: string | null
  jobNumber: string | null
  currentPhase: string
  actionCategory: ActionCategory
  priorityLevel: number
  reason: string
  postureSummary: string
  recommendedNextAction: string
  reviewDate: string | null
  daysSinceMeaningfulActivity: number | null
  relatedWarningCount: number
  currentHolder: string | null
  matterType: MatterType
  relatedDeadlineId?: number | null
}

export type DiscoveryControlCaseRef = {
  caseId: number
  caseName: string
  caseNumber: string | null
  strategy: string
  nextDecision: string | null
  nextReviewDate: string | null
}

export type DiscoveryControlSummary = {
  strategyNotSelected: number
  strategySelectedNotServed: number
  responsesOverdue: number
  responsesReceivedNotReviewed: number
  deficienciesUnresolved: number
  depositionDecisionPending: number
  cutoffApproaching: number
  complete: number
  noDiscoveryNeeded: number
  casesByCondition: Record<string, DiscoveryControlCaseRef[]>
}

export type MomentumReviewEntry = {
  caseId: number
  caseName: string
  caseNumber: string | null
  momentumStatus: MomentumStatus
  daysSinceMeaningfulActivity: number
  waitingOn: string | null
  waitingFollowUpDate: string | null
}

export type PreFilingTractRow = {
  caseId: number
  tractOrOwnerName: string
  projectName: string | null
  jobNumber: string | null
  county: string | null
  currentHolder: string | null
  pipelineStage: string | null
  dateSentToCurrentHolder: string | null
  priority: string
  nextReviewDate: string | null
  currentIssue: string | null
  lastFollowUpDate: string | null
  lastUpdated?: string | null
  flagReason: string | null
}

export type FilingPipelineView = {
  myDesk: PreFilingTractRow[]
  waiting: PreFilingTractRow[]
  allPipeline: PreFilingTractRow[]
}

export type TrialWatchEntry = {
  caseId: number
  caseName: string
  caseNumber: string | null
  trialDate: string | null
  daysUntilTrial: number | null
  deposit: number | null
  stateAppraisal: number | null
  ownerAppraisal: number | null
  ownerDemand: number | null
  lastOffer: number | null
  settlementAuthority: number | null
  feeComparisonNote: string | null
  discoveryStatus: string
  witnessReadiness: string | null
  exhibitReadiness: string | null
  nextTrialDecision: string | null
}

export type UpcomingDecisionItem = {
  caseId: number
  caseName: string
  decisionType: string
  relevantDate: string | null
  context: string | null
  recommendedPreparationDate: string | null
  status: string
}

export type ProjectWatchRow = {
  projectName: string
  jobNumber: string | null
  tractCount: number
  preFilingCount: number
  filedCount: number
  resolvedCount: number
  onAttorneyDeskCount: number
  stalledCount: number
  earliestTrialDate: string | null
  oldestInactiveMatter: string | null
  sharedIssue: string | null
  nextProjectDecision: string | null
}

export type AttorneyDocketSummary = {
  preFilingMatters: number
  filedMatters: number
  trialTrackMatters: number
  waitingAppropriately: number
  onAttorneysDesk: number
  missingNextReviewDate: number
}

export type AttorneyDashboardResponse = {
  summaryCounts: AttorneyDashboardSummaryCounts
  actionQueue: ActionQueueItem[]
  discoveryControl: DiscoveryControlSummary
  momentumReview: MomentumReviewEntry[]
  filingPipeline: FilingPipelineView
  trialWatch: TrialWatchEntry[]
  upcomingDecisions: UpcomingDecisionItem[]
  projectWatch: ProjectWatchRow[]
  docketSummary: AttorneyDocketSummary
  triageCaseCount: number
}

export type AttorneyDashboardFilters = {
  matterType?: string
  project?: string
  county?: string
  priority?: string
  currentHolder?: string
  stage?: string
  trialTrack?: boolean
  momentumStatus?: string
  search?: string
}

export type DiscoveryPosture = {
  id: number
  rowVersion?: string | null
  caseId: number
  strategy: string
  strategyReason: string | null
  strategySelectedDate: string | null
  discoveryServedDate: string | null
  responsesDueDate: string | null
  responsesReceivedDate: string | null
  responsesReviewedDate: string | null
  discoveryCutoffDate: string | null
  plannedDepositions: string | null
  deficiencyStatus: string | null
  nextDecision: string | null
  nextReviewDate: string | null
  isComplete: boolean
  completionChangedAt: string | null
  completionChangedBy: string | null
  createdAt: string | null
  updatedAt: string | null
}

export type PipelineHandoffRecord = {
  id: number
  rowVersion?: string | null
  caseId: number
  previousHolder: string | null
  newHolder: string
  previousStage: string | null
  newStage: string
  handoffDate: string | null
  nextReviewDate: string | null
  note: string | null
  createdAt: string | null
  createdBy?: string | null
  caseRowVersion?: string | null
}

// The dashboard's metric-tile facet row: the Action Queue's 4 priority levels, in display order.
// Replaces the old 6-key SUMMARY_CARD_KEYS/DashboardSummaryCard filter model - those six abstract
// categories (needsJudgment/stalled/discoveryUnset/onMyDesk/trialTrack/missingNextReview) are
// superseded by priority-level tile filtering here, with the same dimensions still reachable via
// the Case Insight rail (Momentum tab, Discovery tab, Trial tab, and the clickable docket kv rows).
export const PRIORITY_TILES: { level: number; label: string; tone: 'danger' | 'warn' | 'default' }[] = [
  { level: 1, label: 'Immediate', tone: 'danger' },
  { level: 2, label: 'Attorney decision', tone: 'warn' },
  { level: 3, label: 'Momentum', tone: 'default' },
  { level: 4, label: 'Planned work', tone: 'default' },
]

export const DISCOVERY_STRATEGIES: DiscoveryStrategy[] = [
  'Strategy not selected',
  'No discovery currently needed',
  'Written discovery first',
  'Landowner deposition first',
  'Appraiser discovery first',
  'Limited targeted discovery',
  'Full discovery plan',
  'Awaiting owner appraisal before deciding',
  'Strategy deferred until a stated event',
]

export const PIPELINE_HOLDERS: CurrentHolder[] = ['Legal Assistant', 'Attorney', 'Deputy Chief Counsel', 'Chief Counsel', 'Filing Staff', 'Other']

export const PIPELINE_STAGES: PipelineStage[] = [
  'With Legal Assistant',
  'With Attorney',
  'With Deputy Chief Counsel',
  'With Chief Counsel',
  'Returned for Revision',
  'Approved for Filing',
  'Filed',
]

// Manager/Administrator Dashboard Milestone 4. Mirrors server/CasePlanner.Web.Server/Models/
// DomainModels.cs's PreFilingMilestoneRecord and SettlementAuthorityRequestRecord field-for-field.

export type PreFilingMilestone =
  | 'PleadingsPackageSent'
  | 'ChiefCounselSignaturesReceived'
  | 'DeclarationOfTakingSentToDirector'
  | 'DirectorSignatureReceived'

export type PreFilingMilestoneRecord = {
  id: number
  caseId: number
  milestone: string
  isMarked: boolean
  occurredDate?: string | null
  markedAt?: string | null
  markedByUserId?: string | null
  markedByDisplay?: string | null
  markedByRole?: string | null
  note?: string | null
  // Historical: shared by every row a single bulk-mark action touched, back when the Bulk Mark
  // Milestones feature existed (since removed). Null for every mark going forward.
  batchId?: string | null
  rowVersion?: string | null
}

// Final implementation, item 2: an unstructured, append-only review-note log - see
// server/CasePlanner.Web.Server/Models/DomainModels.cs's ReviewNoteRecord doc comment for the full
// rationale. reviewerName/reviewerRole are free text; decision is a short, lightly-constrained
// string, not a fixed enum.
export type ReviewNoteRecord = {
  id: number
  caseId: number
  reviewerName?: string | null
  reviewerRole?: string | null
  decision: string
  comment?: string | null
  occurredDate: string
  createdAt?: string | null
  createdByUserId?: string | null
  createdByDisplay?: string | null
  createdByRole?: string | null
}

export type CreateReviewNoteRequest = {
  reviewerName?: string
  reviewerRole?: string
  decision: string
  comment?: string
  occurredDate?: string
}

export type SettlementAuthorityRequestStatus = 'Pending' | 'Approved' | 'Denied' | 'InfoRequested'

export type SettlementAuthorityRequestRecord = {
  id: number
  caseId: number
  requestedAmount: number
  requestingAttorney?: string | null
  requestNotes?: string | null
  status: SettlementAuthorityRequestStatus
  grantedAmount?: number | null
  requestedAt: string
  requestedByUserId?: string | null
  requestedByDisplay?: string | null
  // "Recorded" fields - when and by whom the system entry was made.
  decidedAt?: string | null
  decidedByUserId?: string | null
  decidedByDisplay?: string | null
  decidedByRole?: string | null
  decisionComment?: string | null
  // "Granted" fields (Manager Dashboard sign-off consolidation, item 4) - only meaningful when
  // status is 'Approved'. Distinct from the recorded fields above: the grant may have happened
  // outside the system (e.g. verbally or by email) and be entered here after the fact by someone
  // else, so who/when granted can legitimately differ from who/when recorded.
  grantedBy?: string | null
  grantedByRole?: string | null
  grantedDate?: string | null
  // Optional pointer to supporting correspondence/paperwork - free text, any outcome.
  documentReference?: string | null
  rowVersion?: string | null
}

// Fixed, stable four-milestone order for the pre-filing sign-off tracker. The server's
// PreFilingMilestoneGate.Order/Label (CasePlannerRepository.cs) enforces this same order and
// labeling but isn't exposed as an API - this is a deliberate, small client-side duplication of
// that fixed vocabulary rather than a new endpoint just to fetch four static strings.
export const PRE_FILING_MILESTONE_ORDER: PreFilingMilestone[] = [
  'PleadingsPackageSent',
  'ChiefCounselSignaturesReceived',
  'DeclarationOfTakingSentToDirector',
  'DirectorSignatureReceived',
]

const PRE_FILING_MILESTONE_LABELS: Record<PreFilingMilestone, string> = {
  PleadingsPackageSent: 'Pleadings Package Sent',
  ChiefCounselSignaturesReceived: 'Chief Counsel Signatures Received',
  DeclarationOfTakingSentToDirector: 'Declaration of Taking Sent to Director',
  DirectorSignatureReceived: 'Director Signature Received',
}

export function preFilingMilestoneLabel(milestone: string): string {
  return PRE_FILING_MILESTONE_LABELS[milestone as PreFilingMilestone] ?? milestone
}

// Request bodies for the per-case mark/unmark endpoints (POST /api/cases/{caseId}/
// prefiling-milestones/{milestone}/mark|unmark) - not needed by the Manager Dashboard's read-only
// consumers above, only by the case workspace's PreFilingMilestonesPanel, which actually calls them.
export type MarkPreFilingMilestoneRequest = {
  occurredDate: string
  note?: string
}

export type UnmarkPreFilingMilestoneRequest = {
  reason: string
}

// Manager/Administrator Dashboard Milestone 5. Mirrors server/CasePlanner.Web.Server/Models/
// DomainModels.cs's PreFilingMilestoneAgingSummary/-Bucket/-Case field-for-field - the already
// server-aggregated view behind GET /api/prefiling-milestones/aging, consumed by the Approvals
// tab's read-only Filing Status section (never re-derived client-side).

export type PreFilingMilestoneAgingBucket = {
  // One of PRE_FILING_MILESTONE_ORDER, or "None" for a Pipeline case with nothing marked yet.
  milestone: string
  count: number
}

export type PreFilingMilestoneAgingCase = {
  caseId: number
  jobNumber?: string | null
  tract?: string | null
  caseName?: string | null
  // "None" when no milestone has been marked yet for this case.
  furthestMilestone: string
  // Null when furthestMilestone is "None" - there is no markedAt timestamp to measure from.
  daysSinceMarked?: number | null
}

export type PreFilingMilestoneAgingSummary = {
  buckets: PreFilingMilestoneAgingBucket[]
  cases: PreFilingMilestoneAgingCase[]
}
