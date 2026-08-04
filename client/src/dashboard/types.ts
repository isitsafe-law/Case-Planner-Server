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
  triggerThreshold?: string | null
  postureSummary: string
  recommendedNextAction: string
  reviewDate: string | null
  daysSinceMeaningfulActivity: number | null
  relatedWarningCount: number
  currentHolder: string | null
  matterType: MatterType
  relatedDeadlineId?: number | null
  // Set only for a synthetic entry sourced from an open reminder thread (Legal Assistant Dashboard
  // audit Phase 4) - see ReminderRequestRecord below. Lets the client render a Resolve action
  // instead of the usual case-action controls.
  reminderRequestId?: number | null
  reminderRelatedEventId?: number | null
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
// the Case Insight rail (Review status tab, Discovery tab, Trial tab, and the clickable docket kv rows).
export const PRIORITY_TILES: { level: number; label: string; tone: 'danger' | 'warn' | 'default' }[] = [
  { level: 1, label: 'Immediate', tone: 'danger' },
  { level: 2, label: 'Attorney decision', tone: 'warn' },
  { level: 3, label: 'Review status', tone: 'default' },
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
// DomainModels.cs's PreFilingMilestoneRecord field-for-field.

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
  // Distinct from markedByDisplay/markedByRole above (who acted in the system, e.g. an assistant):
  // the free-text name/role of the real approving party when this milestone represents someone
  // else's sign-off (e.g. Chief Counsel's signature, marked by the assistant on her behalf). Null
  // when the acting user IS the approving party, or simply not recorded.
  onBehalfOfDisplay?: string | null
  onBehalfOfRole?: string | null
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

// The ordinary card and stall detector intentionally use only these two business milestones.
// The broader order remains available for compatibility with legacy history and the server's
// filing-readiness gate; old values are preserved but are not routine workflow prompts.
export const CORE_PRE_FILING_MILESTONE_ORDER: PreFilingMilestone[] = [
  'PleadingsPackageSent',
  'ChiefCounselSignaturesReceived',
]

// ROW intake tracking - mirrors server/CasePlanner.Web.Server/Models/DomainModels.cs's
// CaseRecord.RowIntakeStatus. A different axis from pipelineStage/currentHolder above (the
// internal Legal Assistant -> Attorney -> Deputy Chief Counsel -> Chief Counsel review chain):
// tracks where a tract sits relative to ROW, which happens earlier. The last three values are
// terminal (the tract is never filed); the rest can cycle (e.g. Returned to ROW -> In Title
// Review again on resubmission).
export const ROW_INTAKE_STATUSES = [
  'Received from ROW',
  'In Title Review',
  'Returned to ROW',
  'Ready for Assignment',
  'Acquired by Agreement',
  'Project Revised',
  'Withdrawn',
] as const

export type RowIntakeStatus = (typeof ROW_INTAKE_STATUSES)[number]

export const ROW_INTAKE_TERMINAL_STATUSES: RowIntakeStatus[] = [
  'Acquired by Agreement',
  'Project Revised',
  'Withdrawn',
]

// Mirrors the server's PrefilingReviewEventRecord (Models/DomainModels.cs) field-for-field. Used
// both for the internal holder-chain review log (event_type in Advance/ReturnForRevision/etc.)
// and, since this feature, ROW title-review rounds (event_type="TitleReview", with outcome/
// reviewerDisplay populated and the holder-chain fields left null).
export type PrefilingReviewEventRecord = {
  id: number
  caseId: number
  eventType: string
  priorHolder?: string | null
  newHolder?: string | null
  priorStage?: string | null
  newStage?: string | null
  submittedByHolder?: string | null
  submittedByDisplay?: string | null
  recordedByDisplay?: string | null
  occurredAt: string
  recordedAt: string
  note?: string | null
  overrideReason?: string | null
  outcome?: string | null
  reviewerDisplay?: string | null
}

export type TitleReviewRoundRequest = {
  outcome: RowIntakeStatus
  reviewerDisplay: string
  note?: string
  occurredAt?: string
}

// Legal Assistant Dashboard audit Phase 4 ("Attorney reminder design" - docs/legal-assistant-dashboard-audit.md).
// Mirrors server/CasePlanner.Web.Server/Models/DomainModels.cs's ReminderRequestRecord field-for-field.
// One append-only thread per (caseId, relatedEventId) - relatedEventId is null for a general
// case-level reminder, set when raised from a specific proceeding's preparation page. The latest
// row for a thread is its current state; repeated reminders on an open thread append a FollowUp
// row rather than starting a second thread. No email is ever sent by this feature.
export type ReminderRequestRecord = {
  id: number
  caseId: number
  relatedEventId?: number | null
  eventType: string
  requestedAction?: string | null
  targetAttorneyDisplay?: string | null
  requestedByDisplay?: string | null
  requestedByRole?: string | null
  requestedCompletionDate?: string | null
  followUpDate?: string | null
  comment?: string | null
  status: string
  occurredAt: string
  recordedAt: string
}

export type RequestAttorneyReminderRequest = {
  relatedEventId?: number | null
  requestedAction?: string
  targetAttorneyDisplay?: string
  requestedCompletionDate?: string
  followUpDate?: string
  comment?: string
}

export type ResolveReminderRequest = {
  relatedEventId?: number | null
  comment?: string
}

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
