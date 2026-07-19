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
