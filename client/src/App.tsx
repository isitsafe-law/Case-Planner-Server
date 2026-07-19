import type { FormEvent, ReactNode } from 'react'
import { Fragment, useEffect, useMemo, useRef, useState } from 'react'
import type { AttorneyDashboardFilters, AttorneyDashboardResponse, DiscoveryPosture, PipelineHandoffRecord } from './dashboard/types'
import { PRIORITY_TILES, DISCOVERY_STRATEGIES } from './dashboard/types'
import { ActionQueueFilters } from './dashboard/ActionQueueFilters'
import { ActionQueueRow, type ActionQueueHandlers } from './dashboard/ActionQueueRow'
import { DiscoveryControlPanel } from './dashboard/DiscoveryControlPanel'
import { MomentumReviewPanel } from './dashboard/MomentumReviewPanel'
import { FilingPipelinePanel } from './dashboard/FilingPipelinePanel'
import { PipelineHandoffDialog } from './dashboard/PipelineHandoffDialog'
import { RecordDecisionDialog } from './dashboard/RecordDecisionDialog'
import { TriageWizard } from './TriageWizard'
import { EditHistoryList } from './EditHistoryList'
import { ACTIVITY_TYPE_GROUPS, activityTypeLabel } from './dashboard/RecordDecisionDialog'
import { TrialWatchTable } from './dashboard/TrialWatchTable'
import { ProjectWatchRowCard } from './dashboard/ProjectWatchRowCard'
import { EmptyState } from './dashboard/EmptyState'
import { LoadingSkeleton } from './dashboard/LoadingSkeleton'
import { ErrorState } from './dashboard/ErrorState'
import { getApiAccessToken } from './auth'
import { StatusChip, type StatusTone } from './ui/StatusChip'
import { StatusSelect } from './ui/StatusSelect'
import { TypeChip } from './ui/TypeChip'
import { EmptyState as UiEmptyState } from './ui/EmptyState'
import { FilterBar, FilterChip, FilterSep, FilterSummary } from './ui/FilterBar'
import { Btn } from './ui/Btn'
import { MetricTile } from './ui/MetricTile'
import { Drawer } from './ui/Drawer'

type PageKey = 'dashboard' | 'cases' | 'queues' | 'reports' | 'settings'
type CaseSortColumn = 'caseName' | 'jobNumber' | 'tract' | 'county' | 'stage' | 'track' | 'nextDeadlineDate' | 'attentionStatus' | 'dateOpened' | 'closedDate'
type QueueSortMode = 'dueAsc' | 'dueDesc' | 'caseAsc' | 'caseDesc'
type CaseTabKey = 'overview' | 'work' | 'discovery' | 'documents' | 'riskAnalysis' | 'trialNotebook' | 'notes' | 'servicePublication'
type CasesViewKey = 'list' | 'workspace'
type ThemeMode = 'light' | 'dark' | 'system'
type ModalKind = 'case' | 'deadline' | 'checklist' | 'discovery' | 'comparableSale' | 'witness' | 'exhibit' | 'trialMotion' | 'event'
type ModalMode = 'create' | 'edit'
type FieldErrors = Partial<Record<string, string>>
type SettingsSectionKey = 'appearance' | 'import' | 'diagnostics' | 'storage' | 'about' | 'documentDefaults' | 'referenceLibrary' | 'checklistTemplates' | 'deadlineTemplates' | 'backups' | 'documentPlatformTemplates' | 'issueTags' | 'developer'

type CaseRecord = {
  id: number
  rowVersion?: string | null
  caseNumber: string
  caseName: string
  jobNumber: string
  tract: string
  county: string
  status: string
  caseStatus?: string | null
  statusMappingReview?: boolean
  stage?: string | null
  track?: string | null
  filingDate?: string | null
  dateOfTaking?: string | null
  trialDate?: string | null
  depositAmount?: number | null
  owner?: string | null
  landowner?: string | null
  valuationNotes?: string | null
  settlementNotes?: string | null
  publicationServiceNotes?: string | null
  serviceRequired: boolean
  servicePerfected: boolean
  servicePerfectedDate?: string | null
  serviceDeadline120?: string | null
  serviceDeadlineBasisDate?: string | null
  serviceMethod?: string | null
  serviceNotes?: string | null
  serviceStatus?: string | null
  assignedAttorney?: string | null
  opposingCounsel?: string | null
  appraiser?: string | null
  taxesOwed?: string | null
  fundsWithdrawn?: string | null
  fundsWithdrawnDate?: string | null
  discoveryCompleted?: string | null
  updatedAppraisal?: string | null
  dateOpened?: string | null
  closedDate?: string | null
  projectName?: string | null
  taxOwedAmount?: number | null
  wholePropertyAcres?: number | null
  acquisitionAcres?: number | null
  landownerAppraiserName?: string | null
  additionalDepositAmount?: number | null
  additionalDepositDate?: string | null
  createdAt?: string | null
  updatedAt?: string | null
  checklistTotal?: number
  checklistDone?: number
  attentionStatus?: string
  nextDeadlineDate?: string | null
  nextDeadlineTitle?: string | null
  deferredUntil?: string | null
  deferredReason?: string | null
  deferredAt?: string | null
  deferredBy?: string | null
  currentHolder?: string | null
  nextReviewDate?: string | null
  pipelineStage?: string | null
  shortPostureSummary?: string | null
  trialTrack?: boolean
  nextAction?: string | null
}

type DeadlineHistoryEntry = {
  previousDueDate?: string | null
  newDueDate?: string | null
  reason?: string | null
  changedAt: string
}

type DeadlineItem = {
  id: number
  caseId: number
  title: string
  dueDate?: string | null
  status: string
  notes?: string | null
  sourceType: string
  sourceKind?: string
  sourceTemplateId?: string | null
  sourceTemplateVersion?: number | null
  sourceStage?: string | null
  generatedAt?: string | null
  generatedBy?: string | null
  isManual: boolean
  severity: string
  completedAt?: string | null
  history?: DeadlineHistoryEntry[]
  reasonForChange?: string | null
}

type ChecklistItem = {
  id: number
  caseId: number
  phase: string
  task: string
  dueDate?: string | null
  status: string
  notes?: string | null
  sourceType: string
  sourceKind?: string
  sourceTemplateId?: string | null
  sourceTemplateVersion?: number | null
  sourceStage?: string | null
  generatedAt?: string | null
  generatedBy?: string | null
  isManual: boolean
  completedAt?: string | null
}

type DiscoveryItem = {
  id: number
  caseId: number
  requestTitle?: string | null
  direction: string
  discoveryType: string
  servedDate?: string | null
  dueDate?: string | null
  responseDate?: string | null
  followUpDate?: string | null
  status: string
  assignedTo?: string | null
  notes?: string | null
  escalationNote?: string | null
  goodFaithSentDate?: string | null
  motionToCompelDate?: string | null
}

type ValuationSide = 'ASHC' | 'Landowner'

type ValuationPosition = {
  id: number
  caseId: number
  side: ValuationSide
  appraiserName?: string | null
  appraisedValue?: number | null
  valueDate?: string | null
  methodology?: string | null
  notes?: string | null
  updatedAt?: string | null
}

type ComparableSale = {
  id: number
  caseId: number
  side: ValuationSide
  saleDescription?: string | null
  salePrice?: number | null
  saleDate?: string | null
  sizeAcres?: number | null
  adjustmentNotes?: string | null
  notes?: string | null
}

type Witness = {
  id: number
  caseId: number
  name: string
  side: ValuationSide
  role?: string | null
  contactInfo?: string | null
  subpoenaStatus: string
  outlineNotes?: string | null
  notes?: string | null
}

type Exhibit = {
  id: number
  caseId: number
  label: string
  side: ValuationSide
  description?: string | null
  status: string
  notes?: string | null
}

type TrialMotion = {
  id: number
  caseId: number
  title: string
  filedBy: ValuationSide
  filedDate?: string | null
  status: string
  notes?: string | null
}

type PublicationEntry = {
  id: number
  rowVersion?: string | null
  caseId: number
  publicationNumber: string
  publicationDate?: string | null
  newspaper?: string | null
  proofFiled: boolean
  proofFiledDate?: string | null
  serviceResolved: boolean
  notes?: string | null
}

const serviceLogStatuses = ['Served', 'Not Served', 'Attempted', 'Refused'] as const

const issueTagCategories = ['Valuation', 'Parties', 'Procedure', 'Trial'] as const

const documentTemplateCategories = ['Discovery', 'Judgment', 'Settlement'] as const

type ServiceLogEntry = {
  id: number
  caseId: number
  partyName: string
  status: string
  method?: string | null
  eventDate?: string | null
  notes?: string | null
  createdAt?: string | null
  updatedAt?: string | null
}

type PublicationRecord = {
  caseId: number
  rowVersion?: string | null
  firstPublicationDate?: string | null
  secondPublicationDate?: string | null
  publicationName?: string | null
  markedPerfected: boolean
  lastUpdatedAt?: string | null
  lastUpdatedBy?: string | null
  overrideMissingPublicationName?: boolean
}

type DeadlineTemplate = { id:number; rowVersion?:string|null; name:string; triggerField:string; offsetDays:number; title:string; severity:string; track:string; active:boolean }
type WorkTemplateCandidate = { kind:'Task'|'Deadline'; templateId:string; templateVersion:number; title:string; stage:string; track:string; dueDate?:string|null; severity?:string|null; isDuplicate:boolean; duplicateReason?:string|null; selected?:boolean; allowDuplicate?:boolean }

type ServiceStatusSummary = {
  serviceRequired: boolean
  servicePerfected: boolean
  servicePerfectedDate?: string | null
  serviceDeadline120?: string | null
  serviceDeadlineBasisDate?: string | null
  serviceMethod?: string | null
  serviceStatus?: string | null
  serviceNotes?: string | null
  warningLevel: string
  warningText: string
  daysRemaining?: number | null
  serviceDeadlineCalculated: boolean
  publicationDate?: string | null
  newspaper?: string | null
  proofFiledDate?: string | null
  publicationEntryExists: boolean
  publicationNotes?: string | null
}

type ServiceQueueItem = {
  caseId: number
  caseName: string
  caseNumber: string
  jobNumber: string
  tract: string
  county: string
  filingDate?: string | null
  serviceDeadlineBasisDate?: string | null
  serviceDeadline120?: string | null
  daysRemaining?: number | null
  servicePerfected: boolean
  servicePerfectedDate?: string | null
  serviceMethod?: string | null
  serviceStatus?: string | null
  notesPreview?: string | null
  warningLevel: string
  warningText: string
}

type IssueTag = {
  id: number
  name: string
  description?: string | null
  category?: string | null
}

type CaseIssueTag = {
  id: number
  caseId: number
  issueTagId: number
  tagName: string
  category?: string | null
  description?: string | null
  notes?: string | null
  rowVersion?: string | null
}

type DocumentExport = {
  id: number
  caseId: number
  documentType: string
  documentTitle: string
  outputPath: string
  createdAt: string
  status: string
  qaStatus: string
  qaNotes?: string | null
  errorMessage?: string | null
  contentText?: string | null
  baseTemplateVersion?: string | null
  issueTagVersions?: string | null
  mergeFieldValues?: string | null
  isDraft?: boolean
  isFinalized?: boolean
  rowVersion?: string | null
}

type CaseNote = {
  id: number
  caseId: number
  title: string
  body: string
  createdAt: string
  updatedAt: string
}

const eventTypes = ['Hearing', 'Deposition', 'Mediation', 'Filing Deadline', 'Other'] as const

type Hearing = {
  id: number
  caseId: number
  title: string
  eventType?: string | null
  hearingDate?: string | null
  location?: string | null
  description?: string | null
  createdAt: string
  updatedAt: string
}

type ChecklistTemplateItem = {
  id: number
  rowVersion?: string | null
  templateId: number
  task: string
  phase?: string | null
  sortOrder: number
  dueOffsetDays?: number | null
}

type ChecklistTemplate = {
  id: number
  rowVersion?: string | null
  name: string
  triggerType: string
  stage?: string | null
  issueTagName?: string | null
  track: string
  active: boolean
  items: ChecklistTemplateItem[]
}

type BackupInfo = {
  fileName: string
  sizeBytes: number
  createdAt: string
}

type AttentionItem = {
  kind: string
  caseId: number
  itemId?: number | null
  caseName: string
  caseNumber: string
  summary: string
  dueDate?: string | null
  targetTab: CaseTabKey
}

type DashboardTriageEntry = {
  caseId: number
  caseName: string
  caseNumber: string
  category: string
  reason: string
  timing: string
  stage?: string | null
  track?: string | null
  nextAction: string
  dueDate?: string | null
  priorityScore: number
  matchedCategories: string[]
}

type DashboardData = {
  overdueDeadlines: number
  dueIn7Days: number
  dueIn30Days: number
  upcomingTrials: number
  discoveryDue: number
  discoveryFollowUps: number
  checklistDueSoon: number
  serviceDueSoon: number
  serviceOverdue: number
  casesWithoutPerfectedService: number
  missingServiceDeadline: number
  casesNeedingReview: number
  activeCaseCount: number
  casesUrgentCount: number
  casesAttentionCount: number
  casesUnconfirmedCount: number
  casesStalledCount: number
  casesOnTrackCount: number
  attentionCases: CaseRecord[]
  todaysAgenda: AttentionItem[]
  upcomingDates: AttentionItem[]
  triageQueue: DashboardTriageEntry[]
  needsActionNowCount: number
  serviceRiskCount: number
  hardDeadlinesSoonCount: number
  courtEventsSoonCount: number
  blockedCount: number
  staleReviewCount: number
}

type WorkspaceResponse = {
  case: CaseRecord
  deadlines: DeadlineItem[]
  checklistItems: ChecklistItem[]
  discoveryItems: DiscoveryItem[]
  publicationEntries: PublicationEntry[]
  publication: PublicationRecord
  serviceLogEntries: ServiceLogEntry[]
  caseIssueTags: CaseIssueTag[]
  availableIssueTags: IssueTag[]
  caseNotes: CaseNote[]
  hearings: Hearing[]
  documentExports: DocumentExport[]
  serviceStatus: ServiceStatusSummary
}

type DiagnosticsSnapshot = {
  appName: string
  version: string
  databaseProvider: string
  databaseArchitectureNote: string
  databasePath: string
  databaseWritable: boolean
  backupsWritable: boolean
  exportsWritable: boolean
  logsWritable: boolean
  writeSafetyOk: boolean
  writeSafetyMessage: string
  caseCount: number
  deadlineCount: number
  checklistCount: number
  discoveryCount: number
  documentExportCount: number
  lastImportResult?: string | null
  lastDocumentGenerationResult?: string | null
  latestLogPath?: string | null
  sampleDataExists: boolean
  folders: Record<string, string>
}

type ImportResult = {
  rowsRead: number
  created: number
  updated: number
  skipped: number
  errors: string[]
  info?: string[]
}

type OrgDefaults = {
  attorneyName: string
  barNumber: string
  phone: string
  email: string
  addressLine1: string
  addressLine2: string
  divisionHeadName: string
  rowSectionHeadName: string
  chiefLegalCounselName: string
}


type TemplateTag = {
  key: string
  label: string
  category: string
  description: string
}

// Build-plan step 4 (unified case UI): the new document-platform generation flow. Deliberately
// separate types from UnifiedDocumentCatalogEntry/DocGenState above rather than reusing them -
// this is the new pipeline being proven out, not a retrofit of the old one.
type DocumentGenerationChecklistItem = {
  sectionKey: string
  label: string
  description?: string | null
  issueTagName?: string | null
  isDefaultChecked: boolean
  overlapWarnings: string[]
}
type DocumentGenerationChecklist = {
  templateKey: string
  title: string
  templateVersion: number
  sections: DocumentGenerationChecklistItem[]
  runtimeInputs: { fieldKey: string; label: string; fieldType: string; isRequired: boolean }[]
}
type DocumentGenerationResult = {
  generationId: number
  outputPath: string
  sectionsIncluded: string[]
  missingFields: string[]
}

// Feeds the unified "Generated Documents" history - merged client-side with the legacy
// document_exports list, not migrated into one schema (see build-plan step 7 follow-up).
type DocumentGenerationHistoryItem = {
  id: number
  templateTitle: string
  outputPath: string
  renderedAt: string
  generatedBy?: string | null
  isDraft: boolean
  isFinalized: boolean
  missingFields: string[]
}

// Build-plan step 5 (unified Settings UI): Document Templates admin + Issue Tags admin.
type DocumentTemplateSection = {
  sectionKey: string
  label: string
  description?: string | null
  issueTagName?: string | null
  sortOrder: number
}
type DocumentSectionOverlapPair = { sectionAKey: string; sectionBKey: string; note?: string | null }
type DocumentRuntimeInput = { fieldKey: string; label: string; fieldType: string; isRequired: boolean; sortOrder: number }
type DocumentTemplateVersionInfo = {
  id: number
  version: number
  storagePath: string
  tokens: string[]
  unknownTokens: string[]
  isActive: boolean
  createdAt: string
  createdBy?: string | null
}
type DocumentTemplateAdminSummary = {
  template: { id: number; templateKey: string; title: string; description?: string | null; category: string; isBuiltin: boolean }
  activeVersion: DocumentTemplateVersionInfo | null
  versions: DocumentTemplateVersionInfo[]
  sections: DocumentTemplateSection[]
  overlaps: DocumentSectionOverlapPair[]
  runtimeInputs: DocumentRuntimeInput[]
  lintIssues: string[]
}
type IssueTagUsage = { tagName: string; templateTitles: string[] }

type UpcomingWorkType = 'task' | 'deadline' | 'discovery' | 'service' | 'hearing'
type UpcomingWorkItem = {
  key: string
  caseId: number
  caseName: string
  title: string
  type: UpcomingWorkType
  dueDate?: string | null
  source?: DeadlineItem | ChecklistItem | DiscoveryItem | ServiceQueueItem | Hearing
  tab: CaseTabKey
}

// The upcoming-work API still emits the pre-consolidation tab keys ('deadlines', 'checklist',
// 'hearings', 'details'); fold them onto the 8-tab workspace. Service items land on the
// Service & Publication tab (their factual record); everything else merged into Work.
function normalizeUpcomingWorkTab(tab: string, type: UpcomingWorkType): CaseTabKey {
  switch (tab) {
    case 'deadlines':
    case 'checklist':
    case 'hearings':
      return 'work'
    case 'details':
      return type === 'service' ? 'servicePublication' : 'overview'
    case 'overview':
    case 'work':
    case 'discovery':
    case 'documents':
    case 'riskAnalysis':
    case 'trialNotebook':
    case 'notes':
    case 'servicePublication':
      return tab
    default:
      return 'overview'
  }
}

type ReferenceDocument = {
  key: string
  title: string
  description: string
  text: string
}

// Who made this offer/holds this position.
type OfferMaker = 'ASHC' | 'Landowner'

type RiskAnalysisRowInput = {
  rowKey: string
  label: string
  offerMaker: OfferMaker
  includeSplit: boolean
  justCompensation: number | null
  landownerFeesCosts: number
  ashcCosts: number
  hourlyFeesRisk: number
}

type RiskAnalysisRowResult = RiskAnalysisRowInput & {
  isSplit: boolean
  amountAboveInitialDeposit: number | null
  interestOnOverage: number | null
  subtotal: number | null
  contingencyFee: number | null
  totalRiskHourly: number | null
  hourlyRiskStatus: string
  totalRiskContingency: number | null
  note?: string | null
}

type RiskAnalysisResult = {
  id: number
  caseId: number
  narrative?: string | null
  initialDeposit: number
  additionalDeposit: number
  totalDeposited: number
  daysSinceFiling: number | null
  analysisDate: string
  interestRate: number
  contingencyFeePercent: number
  rows: RiskAnalysisRowResult[]
  createdAt: string
  updatedAt: string
}
type RiskAnalysisHistoryRecord = { id: number; caseId: number; analysisDate: string; formulaVersion: string; interestRate: number; contingencyFeePercent: number; keyScenarioLabel?: string | null; keyScenarioValue?: number | null; keyScenarioOrder?: number | null; narrative?: string | null; rows: RiskAnalysisRowInput[]; createdAt: string }

// The office's "Old Offers" free-text log - separate from the fixed-slot ledger above, for
// tracking additional/historical offers and counteroffers as negotiation continues.
type RiskAnalysisOfferLogEntry = {
  id: number
  caseId: number
  offerDate: string | null
  party: string
  amount: number | null
  updatedAt: string | null
}

type ActivityLogHistoryEntry = {
  id: number
  activityId: number
  previousType: string | null
  newType: string | null
  previousOccurredAt: string | null
  newOccurredAt: string | null
  previousNotes: string | null
  newNotes: string | null
  reason: string | null
  createdAt: string | null
}

type ActivityLogEntry = {
  id: number
  rowVersion?: string | null
  caseId: number
  activityType: string
  isMeaningful: boolean
  occurredAt: string
  notes: string | null
  createdAt: string | null
  history: ActivityLogHistoryEntry[]
}

type RiskNarrativeManualInputs = {
  propertyDescription: string
  tceDescription: string
  highestAndBestUse: string
  ourAppraisalLandBefore: number | null
  ourAppraisalPerSfBefore: number | null
  ourAppraisalLandAfter: number | null
  ourAppraisalPerSfAfter: number | null
  defendantAppraisalLandBefore: number | null
  defendantAppraisalPerSfBefore: number | null
  defendantAppraisalLandAfter: number | null
  defendantAppraisalPerSfAfter: number | null
  ashcOfferDate: string
  feeAdjustmentAmount: number | null
  counterofferDate: string
  settlementAmount: number | null
  trialFeeLow: number | null
  trialFeeHigh: number | null
}

function emptyRiskNarrativeInputs(): RiskNarrativeManualInputs {
  return {
    propertyDescription: '', tceDescription: '', highestAndBestUse: '',
    ourAppraisalLandBefore: null, ourAppraisalPerSfBefore: null, ourAppraisalLandAfter: null, ourAppraisalPerSfAfter: null,
    defendantAppraisalLandBefore: null, defendantAppraisalPerSfBefore: null, defendantAppraisalLandAfter: null, defendantAppraisalPerSfAfter: null,
    ashcOfferDate: '', feeAdjustmentAmount: null, counterofferDate: '', settlementAmount: null, trialFeeLow: null, trialFeeHigh: null,
  }
}

// The ledger is a fixed 5-row structure matching the office's real Risk Analysis workbook
// exactly: two static rows (Opinion of Value, Appraisal) plus three offer/counteroffer slots.
// Each slot's "Source" cell is a dropdown in the real spreadsheet (a data-validation list
// directly on the label cell) - RISK_ANALYSIS_SLOT_OPTIONS mirrors those exact option strings so
// selecting one sets both the row's label and its offerMaker.
const RISK_ANALYSIS_SLOT_OPTIONS: Record<string, string[]> = {
  AshcFirstOffer: ['LANDOWNER FIRST OFFER', 'ASHC FIRST OFFER'],
  AshcCounteroffer: ["LANDOWNER'S COUNTEROFFER", 'ASHC COUNTEROFFER'],
  LandownerCounteroffer: ["LANDOWNER'S COUNTEROFFER", 'ASHC COUNTEROFFER'],
}

const HOURLY_FEE_RISK_OPTIONS = [20000, 25000, 30000, 35000, 40000, 45000, 50000, 55000, 60000, 65000, 70000, 75000, 80000, 85000, 90000]

function emptyServiceLogEntry(caseId: number): ServiceLogEntry {
  return { id: 0, caseId, partyName: '', status: 'Not Served', method: '', eventDate: '', notes: '' }
}

function emptyPublicationEntry(caseId: number): PublicationEntry {
  return { id: 0, caseId, publicationNumber: '', publicationDate: '', newspaper: '', proofFiled: false, proofFiledDate: '', serviceResolved: false, notes: '' }
}

function offerMakerFromLabel(label: string): OfferMaker {
  return label.toUpperCase().includes('ASHC') ? 'ASHC' : 'Landowner'
}

function emptyOfferLogEntry(caseId: number): RiskAnalysisOfferLogEntry {
  return { id: 0, caseId, offerDate: null, party: '', amount: null, updatedAt: null }
}

function emptyRiskAnalysisRow(rowKey: string, label: string, offerMaker: OfferMaker, includeSplit: boolean): RiskAnalysisRowInput {
  return { rowKey, label, offerMaker, includeSplit, justCompensation: null, landownerFeesCosts: 0, ashcCosts: 0, hourlyFeesRisk: 40000 }
}

// Starter rows for a brand-new case with no saved ledger yet.
function defaultRiskAnalysisRows(): RiskAnalysisRowInput[] {
  return [
    emptyRiskAnalysisRow('LandownerOpinionOfValue', "LANDOWNER'S OPINION OF VALUE", 'Landowner', false),
    emptyRiskAnalysisRow('LandownerAppraisal', "LANDOWNER'S APPRAISAL", 'Landowner', false),
    emptyRiskAnalysisRow('AshcFirstOffer', 'ASHC FIRST OFFER', 'ASHC', true),
    emptyRiskAnalysisRow('AshcCounteroffer', 'ASHC COUNTEROFFER', 'ASHC', true),
    emptyRiskAnalysisRow('LandownerCounteroffer', "LANDOWNER'S COUNTEROFFER", 'Landowner', true),
  ]
}

type ApiError = {
  error?: string
  message?: string
}

const navItems: { key: PageKey; label: string }[] = [
  { key: 'dashboard', label: 'Dashboard' },
  { key: 'cases', label: 'Cases' },
  { key: 'queues', label: 'Work Queues' },
  { key: 'reports', label: 'Reports' },
  { key: 'settings', label: 'Settings' },
]

const reportColumnOptions = [
  { key: 'caseName', label: 'Case name' },
  { key: 'caseNumber', label: 'Case number' },
  { key: 'county', label: 'County' },
  { key: 'jobNumber', label: 'Job number' },
  { key: 'tract', label: 'Tract' },
  { key: 'projectName', label: 'Project' },
  { key: 'caseStatus', label: 'Case status' },
  { key: 'currentHolder', label: 'Current holder' },
  { key: 'nextAction', label: 'Next action' },
  { key: 'nextReviewDate', label: 'Next review' },
  { key: 'trialDate', label: 'Trial date' },
  { key: 'dateOpened', label: 'Date opened' },
  { key: 'closedDate', label: 'Date closed' },
  { key: 'caseAgeDays', label: 'Age / duration (days)' },
] as const
type ReportColumnKey = typeof reportColumnOptions[number]['key']

function lifecycleDays(opened?: string | null, closed?: string | null): number | null {
  if (!opened) return null
  const start = new Date(`${opened}T00:00:00`)
  const end = closed ? new Date(`${closed}T00:00:00`) : new Date()
  const days = Math.floor((end.getTime() - start.getTime()) / 86400000)
  return Number.isFinite(days) && days >= 0 ? days : null
}

function reportCellValue(record: CaseRecord, column: ReportColumnKey): string {
  if (column === 'caseAgeDays') {
    const days = lifecycleDays(record.dateOpened, record.closedDate)
    return days == null ? '' : String(days)
  }
  const value = record[column]
  return value == null ? '' : String(value)
}

const caseTabs: { key: CaseTabKey; label: string }[] = [
  { key: 'overview', label: 'Overview' },
  { key: 'work', label: 'Work' },
  { key: 'discovery', label: 'Discovery' },
  { key: 'servicePublication', label: 'Service & Publication' },
  { key: 'riskAnalysis', label: 'Valuation & Risk' },
  { key: 'trialNotebook', label: 'Trial' },
  { key: 'documents', label: 'Documents' },
  { key: 'notes', label: 'Notes' },
]

// caseStatus -> StatusChip tone for the workspace header chip. Falls back to neutral for
// statuses that don't map cleanly onto ok/warn/danger/primary semantics (Pipeline, Triage, and
// anything not yet in consolidatedCaseStatuses).
function caseStatusTone(status?: string | null): StatusTone {
  switch (status) {
    case 'Filed / Service Pending': return 'warn'
    case 'Settlement Pending': return 'warn'
    case 'Active Litigation': return 'primary'
    case 'Trial Preparation': return 'ok'
    case 'Resolved / Closed': return 'ok'
    default: return 'neutral'
  }
}

const settingsSections: { key: SettingsSectionKey; label: string }[] = [
  { key: 'appearance', label: 'Appearance' },
  { key: 'import', label: 'Import' },
  { key: 'diagnostics', label: 'Diagnostics' },
  { key: 'storage', label: 'Storage / Paths' },
  { key: 'documentDefaults', label: 'Document Defaults' },
  { key: 'checklistTemplates', label: 'Checklist Templates' },
  { key: 'deadlineTemplates', label: 'Deadline Templates' },
  { key: 'documentPlatformTemplates', label: 'Document Templates' },
  { key: 'issueTags', label: 'Issue Tags' },
  { key: 'referenceLibrary', label: 'Reference Library' },
  { key: 'backups', label: 'Backups' },
  { key: 'about', label: 'About / IT Notes' },
  // Dev-only - strip this section (and the Developer settings category below) before a real release.
  { key: 'developer', label: 'Developer' },
]

const settingsCategories: { label: string; sections: SettingsSectionKey[] }[] = [
  { label: 'Appearance', sections: ['appearance'] },
  { label: 'Case Workflow', sections: ['documentDefaults', 'referenceLibrary'] },
  { label: 'Tasks and Deadlines', sections: ['checklistTemplates', 'deadlineTemplates'] },
  { label: 'Document Templates', sections: ['documentPlatformTemplates', 'issueTags'] },
  { label: 'Data Management', sections: ['import', 'backups', 'storage'] },
  { label: 'Diagnostics and Help', sections: ['diagnostics', 'about'] },
  // Dev-only category - strip before a real release.
  { label: 'Developer', sections: ['developer'] },
]

const caseStages = [
  'Intake & Filing',
  'Service',
  'Discovery & Evaluation',
  'Trial Track',
  'Resolved',
]
const caseTracks = ['Contested', 'Settlement', 'Default', 'Friendly']
const consolidatedCaseStatuses = ['Pipeline', 'Filed / Service Pending', 'Active Litigation', 'Settlement Pending', 'Trial Preparation', 'Resolved / Closed', 'Triage']
const checklistWorkflowStatuses = consolidatedCaseStatuses
const deadlineStatuses = ['Open', 'Done', 'Reopened']
const checklistStatuses = ['Not Started', 'In Progress', 'Done', 'N/A', 'Reopened']
const modalKindLabels: Record<ModalKind, string> = {
  case: 'Case',
  deadline: 'Deadline',
  checklist: 'Task',
  discovery: 'Discovery Item',
  comparableSale: 'Comparable Sale',
  witness: 'Witness',
  exhibit: 'Exhibit',
  trialMotion: 'Trial Motion',
  event: 'Event',
}

// Section nav for the sectioned case editor drawer - order matches the visual section order below.
type CaseEditorSectionKey = 'identity' | 'people' | 'dates' | 'financial' | 'service' | 'notes'
const caseEditorSections: { key: CaseEditorSectionKey; label: string }[] = [
  { key: 'identity', label: 'Identity' },
  { key: 'people', label: 'People' },
  { key: 'dates', label: 'Dates' },
  { key: 'financial', label: 'Financial & Property' },
  { key: 'service', label: 'Service' },
  { key: 'notes', label: 'Notes' },
]

const discoveryStatuses = ['Waiting for Responses', 'Follow-Up Needed', 'Responses Received', 'Complete', 'Reopened']
const subpoenaStatuses = ['Not Needed', 'Not Served', 'Served', 'Confirmed']
const exhibitStatuses = ['Pre-Labeled', 'Offered', 'Admitted', 'Excluded']
const motionStatuses = ['Pending', 'Granted', 'Denied', 'Withdrawn']
const themeStorageKey = 'ardot-case-planner-theme'
const arkansasCounties = [
  'Arkansas', 'Ashley', 'Baxter', 'Benton', 'Boone', 'Bradley', 'Calhoun', 'Carroll', 'Chicot', 'Clark', 'Clay', 'Cleburne',
  'Cleveland', 'Columbia', 'Conway', 'Craighead', 'Crawford', 'Crittenden', 'Cross', 'Dallas', 'Desha', 'Drew', 'Faulkner',
  'Franklin', 'Fulton', 'Garland', 'Grant', 'Greene', 'Hempstead', 'Hot Spring', 'Howard', 'Independence', 'Izard', 'Jackson',
  'Jefferson', 'Johnson', 'Lafayette', 'Lawrence', 'Lee', 'Lincoln', 'Little River', 'Logan', 'Lonoke', 'Madison', 'Marion',
  'Miller', 'Mississippi', 'Monroe', 'Montgomery', 'Nevada', 'Newton', 'Ouachita', 'Perry', 'Phillips', 'Pike', 'Poinsett',
  'Polk', 'Pope', 'Prairie', 'Pulaski', 'Randolph', 'Saline', 'Scott', 'Searcy', 'Sebastian', 'Sevier', 'Sharp', 'St. Francis',
  'Stone', 'Union', 'Van Buren', 'Washington', 'White', 'Woodruff', 'Yell',
]

async function api<T>(url: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers ?? {})
  const accessToken = await getApiAccessToken()
  if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`)
  if (!headers.has('Content-Type') && init?.body && !(init.body instanceof FormData)) {
    headers.set('Content-Type', 'application/json')
  }

  const response = await fetch(url, { ...init, headers })
  if (!response.ok) {
    const text = await response.text()
    let parsed: ApiError | null = null
    try {
      parsed = JSON.parse(text) as ApiError
    } catch {
      parsed = null
    }
    throw new Error(parsed?.error || parsed?.message || text || `Request failed: ${response.status}`)
  }

  const body = await response.text()
  return (body ? JSON.parse(body) : null) as T
}

function phaseRank(phase: string): number {
  const stageIndex = caseStages.indexOf(phase)
  if (stageIndex >= 0) return stageIndex
  if (!phase || phase === 'General') return 900
  return 500
}

function sortChecklistForDisplay(items: ChecklistItem[]): ChecklistItem[] {
  return [...items].sort((a, b) => {
    const rankDiff = phaseRank(a.phase) - phaseRank(b.phase)
    if (rankDiff !== 0) return rankDiff
    if (a.phase !== b.phase) return a.phase.localeCompare(b.phase)
    if (a.isManual !== b.isManual) return a.isManual ? 1 : -1
    return a.id - b.id
  })
}

function emptyCase(): CaseRecord {
  return {
    id: 0,
    caseNumber: '',
    caseName: '',
    jobNumber: '',
    tract: '',
    county: '',
    status: 'Pipeline',
    stage: 'Intake & Filing',
    track: 'Contested',
    filingDate: '',
    dateOpened: new Date().toISOString().slice(0, 10),
    dateOfTaking: '',
    trialDate: '',
    depositAmount: null,
    owner: '',
    landowner: '',
    valuationNotes: '',
    settlementNotes: '',
    publicationServiceNotes: '',
    serviceRequired: true,
    servicePerfected: false,
    servicePerfectedDate: '',
    serviceDeadline120: '',
    serviceDeadlineBasisDate: '',
    serviceMethod: '',
    serviceNotes: '',
    serviceStatus: '',
    currentHolder: 'Legal Assistant',
    nextReviewDate: '',
    pipelineStage: '',
    shortPostureSummary: '',
    createdAt: '',
    updatedAt: '',
  }
}

function emptyOrgDefaults(): OrgDefaults {
  return {
    attorneyName: '',
    barNumber: '',
    phone: '',
    email: '',
    addressLine1: '',
    addressLine2: '',
    divisionHeadName: '',
    rowSectionHeadName: '',
    chiefLegalCounselName: '',
  }
}

function emptyDeadline(caseId = 0): DeadlineItem {
  return { id: 0, caseId, title: '', dueDate: '', status: 'Open', notes: '', sourceType: 'Manual', isManual: true, severity: 'normal', reasonForChange: '' }
}

const deadlineSeverities = ['normal', 'soft', 'urgent', 'critical']

function emptyChecklist(caseId = 0): ChecklistItem {
  return { id: 0, caseId, phase: 'General', task: '', dueDate: '', status: 'Not Started', notes: '', sourceType: 'Manual', isManual: true }
}

function emptyDiscovery(caseId = 0): DiscoveryItem {
  return {
    id: 0,
    caseId,
    requestTitle: '',
    direction: 'Served by Us',
    discoveryType: 'Interrogatories',
    servedDate: '',
    dueDate: '',
    responseDate: '',
    followUpDate: '',
    status: 'Waiting for Responses',
    assignedTo: '',
    notes: '',
    escalationNote: '',
    goodFaithSentDate: '',
    motionToCompelDate: '',
  }
}

function emptyValuationPosition(caseId: number, side: ValuationSide): ValuationPosition {
  return { id: 0, caseId, side, appraiserName: '', appraisedValue: null, valueDate: '', methodology: '', notes: '' }
}

function emptyComparableSale(caseId: number, side: ValuationSide): ComparableSale {
  return { id: 0, caseId, side, saleDescription: '', salePrice: null, saleDate: '', sizeAcres: null, adjustmentNotes: '', notes: '' }
}

function emptyDiscoveryPosture(caseId: number): DiscoveryPosture {
  return {
    id: 0, caseId, strategy: 'Strategy not selected', strategyReason: null, strategySelectedDate: null,
    discoveryServedDate: null, responsesDueDate: null, responsesReceivedDate: null, responsesReviewedDate: null,
    discoveryCutoffDate: null, plannedDepositions: null, deficiencyStatus: null, nextDecision: null,
    nextReviewDate: null, isComplete: false, completionChangedAt: null, completionChangedBy: null, createdAt: null, updatedAt: null,
  }
}

function emptyWitness(caseId: number): Witness {
  return { id: 0, caseId, name: '', side: 'ASHC', role: '', contactInfo: '', subpoenaStatus: 'Not Needed', outlineNotes: '', notes: '' }
}

function emptyExhibit(caseId: number): Exhibit {
  return { id: 0, caseId, label: '', side: 'ASHC', description: '', status: 'Pre-Labeled', notes: '' }
}

function emptyHearing(caseId: number): Hearing {
  return { id: 0, caseId, title: '', eventType: 'Hearing', hearingDate: '', location: '', description: '', createdAt: '', updatedAt: '' }
}

function emptyTrialMotion(caseId: number): TrialMotion {
  return { id: 0, caseId, title: '', filedBy: 'ASHC', filedDate: '', status: 'Pending', notes: '' }
}

function emptyPublication(caseId = 0): PublicationRecord {
  return {
    caseId,
    firstPublicationDate: '',
    secondPublicationDate: '',
    publicationName: '',
    markedPerfected: false,
  }
}

const MONTH_NAMES = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
]

function displayDate(value?: string | null): string {
  if (!value) return '—'
  const match = value.match(/^(\d{4})-(\d{2})-(\d{2})/)
  if (!match) return value
  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  if (month < 1 || month > 12 || day < 1 || day > 31) return value
  return `${MONTH_NAMES[month - 1]} ${day}, ${year}`
}

function matchesUrgency(dateValue: string | null | undefined, urgency: string): boolean {
  if (urgency === 'All Open') return true
  if (!dateValue) return urgency === 'No Due Date'
  if (urgency === 'No Due Date') return false
  const due = DateOnlyFromString(dateValue)
  if (due === null) return false
  const today = DateOnlyFromString(new Date().toISOString().slice(0, 10))!
  const days = due - today
  if (urgency === 'Overdue') return days < 0
  if (urgency === 'Due Today') return days === 0
  if (urgency === 'Due in 7 Days') return days >= 0 && days <= 7
  if (urgency === 'Due in 14 Days') return days >= 0 && days <= 14
  if (urgency === 'Due in 30 Days') return days >= 0 && days <= 30
  return true
}

function DateOnlyFromString(value: string): number | null {
  const match = value.slice(0, 10).match(/^(\d{4})-(\d{2})-(\d{2})$/)
  if (!match) return null
  return Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])) / 86400000
}

const dateTimeFormatter = new Intl.DateTimeFormat('en-US', {
  timeZone: 'America/Chicago',
  month: 'long',
  day: 'numeric',
  year: 'numeric',
  hour: 'numeric',
  minute: '2-digit',
})

function displayDateTime(value?: string | null): string {
  if (!value) return '—'
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) return value
  return `${dateTimeFormatter.format(parsed)} CT`
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

function displayCurrency(value?: number | null): string {
  if (value == null) return 'Not set'
  return `$${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function sortQueueItems<T>(items: T[], mode: QueueSortMode, getCase: (item: T) => string | null | undefined, getDue: (item: T) => string | null | undefined): T[] {
  return [...items].sort((a, b) => {
    const caseA = (getCase(a) || '').toLocaleLowerCase()
    const caseB = (getCase(b) || '').toLocaleLowerCase()
    const dueA = getDue(a) || '9999-12-31'
    const dueB = getDue(b) || '9999-12-31'
    const comparison = mode.startsWith('case') ? caseA.localeCompare(caseB) : dueA.localeCompare(dueB)
    return mode.endsWith('Desc') ? -comparison : comparison
  })
}

function sidePillTone(side: ValuationSide): string {
  return side === 'Landowner' ? 'warn' : 'primary'
}

const attentionLabels: Record<string, string> = {
  urgent: 'Urgent',
  attention: 'Attention',
  unconfirmed: 'Unconfirmed',
  stalled: 'Stalled',
  onTrack: 'On Track',
  closed: 'Closed',
  triage: 'Triage',
}

function caseSortValue(item: CaseRecord, column: CaseSortColumn): string {
  switch (column) {
    case 'caseName': return item.caseName || ''
    case 'jobNumber': return item.jobNumber || ''
    case 'tract': return item.tract || ''
    case 'county': return item.county || ''
    case 'stage': return item.stage || ''
    case 'track': return item.track || 'Contested'
    case 'nextDeadlineDate': return item.nextDeadlineDate || ''
    case 'attentionStatus': return attentionLabels[item.attentionStatus || 'onTrack'] || ''
    case 'dateOpened': return item.dateOpened || ''
    case 'closedDate': return item.closedDate || ''
  }
}

function sortCases(list: CaseRecord[], column: CaseSortColumn, direction: 'asc' | 'desc'): CaseRecord[] {
  const sign = direction === 'asc' ? 1 : -1
  return [...list].sort((a, b) => {
    const aValue = caseSortValue(a, column)
    const bValue = caseSortValue(b, column)
    if (aValue === '' && bValue === '') return 0
    if (aValue === '') return 1
    if (bValue === '') return -1
    return sign * aValue.localeCompare(bValue, undefined, { numeric: true, sensitivity: 'base' })
  })
}

function attentionPillTone(status?: string): string {
  switch (status) {
    case 'urgent': return 'danger'
    case 'attention': return 'warn'
    case 'unconfirmed': return 'primary'
    case 'stalled': return 'primary'
    case 'closed': return 'neutral'
    case 'triage': return 'primary'
    default: return 'success'
  }
}

function discoveryStatusPillTone(status: string): string {
  switch (status) {
    case 'Follow-Up Needed': return 'warn'
    case 'Reopened': return 'danger'
    case 'Complete': return 'success'
    case 'Responses Received': return 'primary'
    default: return 'neutral'
  }
}

function countyOptions(value?: string | null): string[] {
  const trimmed = value?.trim()
  return trimmed && !arkansasCounties.includes(trimmed) ? [trimmed, ...arkansasCounties] : arkansasCounties
}

function normalizeTextValue(value?: string | null): string | null {
  const trimmed = value?.trim()
  return trimmed ? trimmed : null
}

function isDeadlineDone(item: DeadlineItem): boolean {
  return item.status === 'Done' || item.status === 'Complete'
}

function isChecklistDone(item: ChecklistItem): boolean {
  return item.status === 'Done' || item.status === 'Complete' || item.status === 'N/A'
}

// ---- Work Queue (unified) helpers ----------------------------------------

function isQueueDateOverdue(dateValue?: string | null): boolean {
  return matchesUrgency(dateValue, 'Overdue')
}

function serviceWarningTone(warningLevel: string): StatusTone {
  if (warningLevel === 'overdue' || warningLevel === 'missing') return 'danger'
  if (warningLevel === 'urgent' || warningLevel === 'upcoming') return 'warn'
  return 'neutral'
}

function discoveryStatusTone(status: string): StatusTone {
  if (status === 'Complete') return 'ok'
  if (status.includes('Follow-Up')) return 'danger'
  if (status.includes('Waiting')) return 'warn'
  return 'neutral'
}

function deadlineRowTone(item: DeadlineItem): StatusTone {
  if (isDeadlineDone(item)) return 'ok'
  if (isQueueDateOverdue(item.dueDate)) return 'danger'
  return 'primary'
}

function checklistRowTone(item: ChecklistItem): StatusTone {
  if (isChecklistDone(item)) return 'ok'
  if (isQueueDateOverdue(item.dueDate)) return 'danger'
  return 'primary'
}

type QueueRow =
  | { kind: 'service'; key: string; item: ServiceQueueItem }
  | { kind: 'deadline'; key: string; item: DeadlineItem }
  | { kind: 'task'; key: string; item: ChecklistItem }
  | { kind: 'discovery'; key: string; item: DiscoveryItem }
  | { kind: 'event'; key: string; item: Hearing }

function shouldShowRecordValue(value: string | number | boolean | null | undefined): boolean {
  if (typeof value === 'boolean') return true
  if (typeof value === 'number') return true
  if (value == null) return false
  return value.trim().length > 0
}

function formatRecordValue(value: string | number | boolean | null | undefined): string {
  if (typeof value === 'boolean') return value ? 'Yes' : 'No'
  if (typeof value === 'number') return String(value)
  return value ?? ''
}

function isYesLike(value?: string | null): boolean {
  const normalized = value?.trim().toLowerCase()
  return normalized === 'yes' || normalized === 'true' || normalized === '1' || normalized === 'y'
}

function isValidDateValue(value?: string | null): boolean {
  if (!value?.trim()) return true
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false
  const parsed = new Date(`${value}T00:00:00`)
  return !Number.isNaN(parsed.getTime())
}

function normalizeDateValue(value?: string | null): string | null {
  if (!value?.trim()) return null
  if (!isValidDateValue(value)) return null
  return value === '1900-01-01' ? null : value
}

function validateCaseDraft(draft: CaseRecord): { fieldErrors: FieldErrors; summary: string } {
  const fieldErrors: FieldErrors = {}
  if (!draft.caseName.trim()) fieldErrors.caseName = 'Case name is required.'

  for (const field of ['filingDate', 'dateOpened', 'dateOfTaking', 'trialDate', 'closedDate', 'serviceDeadlineBasisDate', 'serviceDeadline120', 'servicePerfectedDate'] as const) {
    if (!isValidDateValue(draft[field])) {
      fieldErrors[field] = 'Enter a valid date in YYYY-MM-DD format.'
    }
  }

  if (draft.depositAmount != null && !Number.isFinite(draft.depositAmount)) {
    fieldErrors.depositAmount = 'Enter a valid amount.'
  }

  return {
    fieldErrors,
    summary: 'Please fix the highlighted case fields before saving.',
  }
}

function validateDeadlineDraft(draft: DeadlineItem): { fieldErrors: FieldErrors; summary: string } {
  const fieldErrors: FieldErrors = {}
  if (!draft.title.trim()) fieldErrors.title = 'Deadline title is required.'
  if (!isValidDateValue(draft.dueDate)) fieldErrors.dueDate = 'Enter a valid date in YYYY-MM-DD format.'
  return { fieldErrors, summary: 'Please fix the highlighted deadline fields before saving.' }
}

function validateChecklistDraft(draft: ChecklistItem): { fieldErrors: FieldErrors; summary: string } {
  const fieldErrors: FieldErrors = {}
  if (!draft.task.trim()) fieldErrors.task = 'Checklist task is required.'
  if (!isValidDateValue(draft.dueDate)) fieldErrors.dueDate = 'Enter a valid date in YYYY-MM-DD format.'
  return { fieldErrors, summary: 'Please fix the highlighted task fields before saving.' }
}

function validateDiscoveryDraft(draft: DiscoveryItem): { fieldErrors: FieldErrors; summary: string } {
  const fieldErrors: FieldErrors = {}
  if (!draft.discoveryType.trim()) fieldErrors.discoveryType = 'Discovery type is required.'
  for (const field of ['servedDate', 'dueDate', 'responseDate', 'followUpDate', 'goodFaithSentDate', 'motionToCompelDate'] as const) {
    if (!isValidDateValue(draft[field])) {
      fieldErrors[field] = 'Enter a valid date in YYYY-MM-DD format.'
    }
  }
  return { fieldErrors, summary: 'Please fix the highlighted discovery fields before saving.' }
}

function App() {
  const [theme, setTheme] = useState<ThemeMode>(() => {
    if (typeof window === 'undefined') return 'light'
    const stored = window.localStorage.getItem(themeStorageKey)
    return stored === 'dark' || stored === 'system' ? stored : 'light'
  })
  const [page, setPage] = useState<PageKey>('dashboard')
  const [shutdownBusy, setShutdownBusy] = useState(false)
  const [reportStatusFilter, setReportStatusFilter] = useState('')
  const [reportCountyFilter, setReportCountyFilter] = useState('')
  const [reportSearch, setReportSearch] = useState('')
  const [reportOpenedFrom, setReportOpenedFrom] = useState('')
  const [reportOpenedTo, setReportOpenedTo] = useState('')
  const [reportPreset, setReportPreset] = useState('')
  const [reportServerRows, setReportServerRows] = useState<CaseRecord[]>([])
  const [reportColumns, setReportColumns] = useState<ReportColumnKey[]>(['caseName', 'caseNumber', 'county', 'caseStatus', 'currentHolder', 'nextAction', 'trialDate'])
  const [reportSortColumn, setReportSortColumn] = useState<ReportColumnKey>('caseName')
  const [reportSortDirection, setReportSortDirection] = useState<'asc' | 'desc'>('asc')
  const [attorneyDashboard, setAttorneyDashboard] = useState<AttorneyDashboardResponse | null>(null)
  const [attorneyDashboardLoading, setAttorneyDashboardLoading] = useState(false)
  const [attorneyDashboardError, setAttorneyDashboardError] = useState('')
  const [attorneyDashboardFilters, setAttorneyDashboardFilters] = useState<AttorneyDashboardFilters>({})
  // Default = the two highest-priority tiles (Immediate + Attorney decision) so the queue starts focused.
  const [activeQueueTiles, setActiveQueueTiles] = useState<Set<number>>(() => new Set([1, 2]))
  const [handoffTarget, setHandoffTarget] = useState<{ caseId: number; caseName: string } | null>(null)
  const [selectedActionQueueIds, setSelectedActionQueueIds] = useState<number[]>([])
  const [bulkDeferOpen, setBulkDeferOpen] = useState(false)
  const [bulkDeferDate, setBulkDeferDate] = useState('')
  const [bulkDeferPreset, setBulkDeferPreset] = useState<'7' | '14' | '30' | 'custom'>('7')
  const [defermentDateEditOpen, setDefermentDateEditOpen] = useState(false)
  const [defermentDateDraft, setDefermentDateDraft] = useState('')
  const [casesView, setCasesView] = useState<CasesViewKey>('list')
  const [caseTab, setCaseTab] = useState<CaseTabKey>('overview')
  // Work tab facet (replaces the old deadlineViewFilter/checklistViewFilter pair): 'open' shows
  // open deadlines + open tasks + all events; 'deadlines'/'tasks' narrow to the open items of that
  // type; 'events' shows all events (no done-ness); 'done' shows done deadlines + done tasks.
  const [workFacet, setWorkFacet] = useState<'open' | 'deadlines' | 'tasks' | 'events' | 'done'>('open')
  const [workFromTemplateOpen, setWorkFromTemplateOpen] = useState(false)
  const [caseMenuOpen, setCaseMenuOpen] = useState(false)
  const caseMenuRef = useRef<HTMLDivElement | null>(null)
  const caseEditorSectionRefs = useRef<Record<CaseEditorSectionKey, HTMLElement | null>>({
    identity: null,
    people: null,
    dates: null,
    financial: null,
    service: null,
    notes: null,
  })
  const [selectedDeadlineIds, setSelectedDeadlineIds] = useState<number[]>([])
  const [selectedChecklistIds, setSelectedChecklistIds] = useState<number[]>([])
  const [bulkDeadlineDueDate, setBulkDeadlineDueDate] = useState('')
  const [bulkChecklistDueDate, setBulkChecklistDueDate] = useState('')
  const [bulkChecklistDueDateOpen, setBulkChecklistDueDateOpen] = useState(false)
  const [dashboard, setDashboard] = useState<DashboardData | null>(null)
  const [diagnostics, setDiagnostics] = useState<DiagnosticsSnapshot | null>(null)
  const [cases, setCases] = useState<CaseRecord[]>([])
  const [allCases, setAllCases] = useState<CaseRecord[]>([])
  const [selectedCaseId, setSelectedCaseId] = useState<number | null>(null)
  const [workspace, setWorkspace] = useState<WorkspaceResponse | null>(null)
  const [queueDeadlines, setQueueDeadlines] = useState<DeadlineItem[]>([])
  const [queueChecklist, setQueueChecklist] = useState<ChecklistItem[]>([])
  const [queueDiscovery, setQueueDiscovery] = useState<DiscoveryItem[]>([])
  const [queueService, setQueueService] = useState<ServiceQueueItem[]>([])
  const [queueHearings, setQueueHearings] = useState<Hearing[]>([])
  const [workQueueFilter, setWorkQueueFilter] = useState<'all' | 'service' | 'deadlines' | 'checklist' | 'discovery' | 'hearings'>('all')
  const [workQueueUrgency, setWorkQueueUrgency] = useState('All Open')
  const [serverUpcomingWorkItems, setServerUpcomingWorkItems] = useState<UpcomingWorkItem[]>([])
  const [serverUpcomingWorkLoaded, setServerUpcomingWorkLoaded] = useState(false)
  const [serviceConditionFilter, setServiceConditionFilter] = useState<'all' | 'missingDeadline' | 'notPerfected' | 'missingBasis'>('all')
  const [workQueueSort, setWorkQueueSort] = useState<'dueAsc' | 'dueDesc' | 'caseAsc' | 'caseDesc'>('dueAsc')
  const [workQueueSearch, setWorkQueueSearch] = useState('')
  const [caseSearch, setCaseSearch] = useState('')
  const [topbarSearch, setTopbarSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState('')
  const [caseStatusFilter, setCaseStatusFilter] = useState('')
  const [countyFilter, setCountyFilter] = useState('')
  const [includeClosed, setIncludeClosed] = useState(false)
  const [caseSortColumn, setCaseSortColumn] = useState<CaseSortColumn | null>(null)
  const [caseListShowLifecycle, setCaseListShowLifecycle] = useState(false)
  const [caseSortDirection, setCaseSortDirection] = useState<'asc' | 'desc'>('asc')
  const [settingsSection, setSettingsSection] = useState<SettingsSectionKey>('appearance')
  const [orgDefaults, setOrgDefaults] = useState<OrgDefaults>(emptyOrgDefaults())
  const [statusMappingReviewCases, setStatusMappingReviewCases] = useState<CaseRecord[]>([])
  const [templateTags, setTemplateTags] = useState<TemplateTag[]>([])
  const [platformChecklist, setPlatformChecklist] = useState<DocumentGenerationChecklist | null>(null)
  const [platformSelectedSections, setPlatformSelectedSections] = useState<string[]>([])
  const [platformRuntimeInputValues, setPlatformRuntimeInputValues] = useState<Record<string, string>>({})
  const [platformCaseTemplateKey, setPlatformCaseTemplateKey] = useState('')
  const [platformGenerationResult, setPlatformGenerationResult] = useState<DocumentGenerationResult | null>(null)
  const [platformGenerationHistory, setPlatformGenerationHistory] = useState<DocumentGenerationHistoryItem[]>([])
  const [platformBusy, setPlatformBusy] = useState(false)
  const [platformTemplates, setPlatformTemplates] = useState<DocumentTemplateAdminSummary[]>([])
  const [selectedPlatformTemplateKey, setSelectedPlatformTemplateKey] = useState<string | null>(null)
  const [platformUploadDraft, setPlatformUploadDraft] = useState({ templateKey: '', title: '', description: '', category: '' })
  const [platformUploadFile, setPlatformUploadFile] = useState<File | null>(null)
  const [platformUploadKeyLocked, setPlatformUploadKeyLocked] = useState(false)
  const [platformConfigDraft, setPlatformConfigDraft] = useState<{ sections: DocumentTemplateSection[]; overlaps: DocumentSectionOverlapPair[]; runtimeInputs: DocumentRuntimeInput[] }>({ sections: [], overlaps: [], runtimeInputs: [] })
  const [newSectionDraft, setNewSectionDraft] = useState({ sectionKey: '', label: '', description: '', issueTagName: '' })
  const [newOverlapDraft, setNewOverlapDraft] = useState({ sectionAKey: '', sectionBKey: '', note: '' })
  const [newRuntimeInputDraft, setNewRuntimeInputDraft] = useState({ fieldKey: '', label: '', fieldType: 'text', isRequired: true })
  const [issueTagUsage, setIssueTagUsage] = useState<IssueTagUsage[]>([])
  const [newIssueTagDraft, setNewIssueTagDraft] = useState({ name: '', description: '', category: '' })
  const [issueTagEditDraft, setIssueTagEditDraft] = useState<{ id: number; name: string; description: string; category: string } | null>(null)
  const [showMergeTagsModal, setShowMergeTagsModal] = useState(false)
  const [mergeTagSearch, setMergeTagSearch] = useState('')
  const [checklistTemplates, setChecklistTemplates] = useState<ChecklistTemplate[]>([])
  const [deadlineTemplates, setDeadlineTemplates] = useState<DeadlineTemplate[]>([])
  const [deadlineTemplateDraft, setDeadlineTemplateDraft] = useState<DeadlineTemplate | null>(null)
  const [workTemplatePicker, setWorkTemplatePicker] = useState<{caseId:number; kind:'Task'|'Deadline'|'All'; items:WorkTemplateCandidate[]}|null>(null)
  const [workTemplateFilter, setWorkTemplateFilter] = useState<'recommended'|'all'|'duplicates'|'tasks'|'deadlines'>('recommended')
  const [expandedWorkTemplateId, setExpandedWorkTemplateId] = useState<string | null>(null)
  const [allIssueTags, setAllIssueTags] = useState<IssueTag[]>([])
  const [expandedTemplateId, setExpandedTemplateId] = useState<number | null>(null)
  const [templateDraft, setTemplateDraft] = useState<ChecklistTemplate | null>(null)
  const [templateItemDraft, setTemplateItemDraft] = useState<ChecklistTemplateItem | null>(null)
  const [backups, setBackups] = useState<BackupInfo[]>([])
  const [riskAnalysisLoadedForCase, setRiskAnalysisLoadedForCase] = useState<number | null>(null)
  const [riskAnalysisNarrative, setRiskAnalysisNarrative] = useState('')
  const [riskAnalysisDate, setRiskAnalysisDate] = useState(() => new Date().toISOString().slice(0, 10))
  const [riskAnalysisInterestRate, setRiskAnalysisInterestRate] = useState(0.06)
  const [riskAnalysisContingencyPercent, setRiskAnalysisContingencyPercent] = useState(0.30)
  const [riskAnalysisRows, setRiskAnalysisRows] = useState<RiskAnalysisRowInput[]>(() => defaultRiskAnalysisRows())
  const [riskAnalysisPreview, setRiskAnalysisPreview] = useState<RiskAnalysisResult | null>(null)
  const [riskAnalysisHistory, setRiskAnalysisHistory] = useState<RiskAnalysisHistoryRecord[]>([])
  const [riskAnalysisEditorOpen, setRiskAnalysisEditorOpen] = useState(false)
  const [riskAnalysisComparison, setRiskAnalysisComparison] = useState<{ left: RiskAnalysisHistoryRecord; right: RiskAnalysisResult } | null>(null)
  const [riskAnalysisSaving, setRiskAnalysisSaving] = useState(false)
  const [offerLog, setOfferLog] = useState<RiskAnalysisOfferLogEntry[]>([])
  const [offerLogDraft, setOfferLogDraft] = useState<RiskAnalysisOfferLogEntry>(() => emptyOfferLogEntry(0))
  const [offerLogFormOpen, setOfferLogFormOpen] = useState(false)
  const [serviceLogEntries, setServiceLogEntries] = useState<ServiceLogEntry[]>([])
  const [serviceLogDraft, setServiceLogDraft] = useState<ServiceLogEntry>(() => emptyServiceLogEntry(0))
  const [serviceLogFormOpen, setServiceLogFormOpen] = useState(false)
  const [servicePerfectedConfirming, setServicePerfectedConfirming] = useState(false)
  const [publicationEntries, setPublicationEntries] = useState<PublicationEntry[]>([])
  const [publicationEntryDraft, setPublicationEntryDraft] = useState<PublicationEntry>(() => emptyPublicationEntry(0))
  const [publicationEntryFormOpen, setPublicationEntryFormOpen] = useState(false)
  const [narrativeInputDraft, setNarrativeInputDraft] = useState<RiskNarrativeManualInputs | null>(null)
  const [narrativeGenerating, setNarrativeGenerating] = useState(false)
  const [activityLog, setActivityLog] = useState<ActivityLogEntry[]>([])
  const [activityLogLoadedForCase, setActivityLogLoadedForCase] = useState<number | null>(null)
  const [editingActivityId, setEditingActivityId] = useState<number | null>(null)
  const [activityEditDraft, setActivityEditDraft] = useState({ activityType: 'Other', occurredAt: '', notes: '', reason: '' })
  const [caseRecordDecisionOpen, setCaseRecordDecisionOpen] = useState(false)
  const [discoveryPosture, setDiscoveryPosture] = useState<DiscoveryPosture | null>(null)
  const [discoveryPostureLoadedForCase, setDiscoveryPostureLoadedForCase] = useState<number | null>(null)
  const [discoveryPostureSaving, setDiscoveryPostureSaving] = useState(false)
  const [templateRegenCaseId, setTemplateRegenCaseId] = useState(0)
  const [templateRegenBusy, setTemplateRegenBusy] = useState(false)
  const [triageWizardOpen, setTriageWizardOpen] = useState(false)
  const [searchSuggestions, setSearchSuggestions] = useState<CaseRecord[]>([])
  const [searchDropdownOpen, setSearchDropdownOpen] = useState(false)
  const [dashboardFiltersOpen, setDashboardFiltersOpen] = useState(false)
  const [dashboardPanelTab, setDashboardPanelTab] = useState<'discovery' | 'momentum' | 'pipeline' | 'trial' | 'projects' | 'docket'>(() => {
    const saved = window.localStorage.getItem('case-insight-tab')
    return saved === 'discovery' || saved === 'momentum' || saved === 'pipeline' || saved === 'trial' || saved === 'projects' || saved === 'docket' ? saved : 'docket'
  })
  const [docketMetricFilter, setDocketMetricFilter] = useState<'preFiling' | 'filed' | 'trial' | 'waiting' | 'desk' | 'missingReview' | null>(null)

  useEffect(() => {
    window.localStorage.setItem('case-insight-tab', dashboardPanelTab)
  }, [dashboardPanelTab])
  const [expandedDiscoveryGroup, setExpandedDiscoveryGroup] = useState<string | null>(null)
  const [expandedDiscoveryItemId, setExpandedDiscoveryItemId] = useState<number | null>(null)
  const [discoveryStrategyEditing, setDiscoveryStrategyEditing] = useState(false)
  const [valuationPositions, setValuationPositions] = useState<ValuationPosition[]>([])
  const [comparableSales, setComparableSales] = useState<ComparableSale[]>([])
  const [valuationDrafts, setValuationDrafts] = useState<Record<ValuationSide, ValuationPosition>>({
    ASHC: emptyValuationPosition(0, 'ASHC'),
    Landowner: emptyValuationPosition(0, 'Landowner'),
  })
  const [editingValuationSide, setEditingValuationSide] = useState<ValuationSide | null>(null)
  const [comparableSaleDraft, setComparableSaleDraft] = useState<ComparableSale>(emptyComparableSale(0, 'ASHC'))
  const [witnesses, setWitnesses] = useState<Witness[]>([])
  const [exhibits, setExhibits] = useState<Exhibit[]>([])
  const [trialMotions, setTrialMotions] = useState<TrialMotion[]>([])
  const [trialNotebookLoadedForCase, setTrialNotebookLoadedForCase] = useState<number | null>(null)
  const [witnessDraft, setWitnessDraft] = useState<Witness>(emptyWitness(0))
  const [exhibitDraft, setExhibitDraft] = useState<Exhibit>(emptyExhibit(0))
  const [trialMotionDraft, setTrialMotionDraft] = useState<TrialMotion>(emptyTrialMotion(0))
  const [referenceLibrary, setReferenceLibrary] = useState<ReferenceDocument[]>([])
  const [expandedRefKey, setExpandedRefKey] = useState<string | null>(null)
  const [referenceEditKey, setReferenceEditKey] = useState<string | null>(null)
  const [referenceEditDraft, setReferenceEditDraft] = useState<ReferenceDocument | null>(null)
  const [noteDraft, setNoteDraft] = useState<CaseNote>({ id: 0, caseId: 0, title: '', body: '', createdAt: '', updatedAt: '' })
  const [hearingDraft, setHearingDraft] = useState<Hearing>({ id: 0, caseId: 0, title: '', hearingDate: '', location: '', description: '', createdAt: '', updatedAt: '' })
  const [message, setMessage] = useState('Loading local workspace...')
  const [errorMessage, setErrorMessage] = useState('')
  const [showAllCaseRecordFields, setShowAllCaseRecordFields] = useState(false)
  const [issueTagMessage, setIssueTagMessage] = useState('')
  const [importResult, setImportResult] = useState<ImportResult | null>(null)
  const [importFileName, setImportFileName] = useState('No file selected.')
  const [importExcelFileName, setImportExcelFileName] = useState('No file selected.')
  const [selectedTagId, setSelectedTagId] = useState(0)
  const [caseDraft, setCaseDraft] = useState<CaseRecord>(emptyCase())
  const [deadlineDraft, setDeadlineDraft] = useState<DeadlineItem>(emptyDeadline())
  const [checklistDraft, setChecklistDraft] = useState<ChecklistItem>(emptyChecklist())
  const [discoveryDraft, setDiscoveryDraft] = useState<DiscoveryItem>(emptyDiscovery())
  const [publicationDraft, setPublicationDraft] = useState<PublicationRecord>(emptyPublication())
  const [activeModal, setActiveModal] = useState<ModalKind | null>(null)
  const [modalMode, setModalMode] = useState<ModalMode>('create')
  const [modalDirty, setModalDirty] = useState(false)
  const [modalErrorSummary, setModalErrorSummary] = useState('')
  const [modalFieldErrors, setModalFieldErrors] = useState<FieldErrors>({})

  useEffect(() => {
    void loadInitial()
  }, [])

  useEffect(() => {
    const handler = (e: BeforeUnloadEvent) => {
      e.preventDefault()
      e.returnValue = ''
    }
    window.addEventListener('beforeunload', handler)
    return () => window.removeEventListener('beforeunload', handler)
  }, [])

  // Type-ahead for the topbar search: debounced fetch against the same /api/cases search the
  // full list uses (matches case number, name, job number, and tract).
  useEffect(() => {
    const query = topbarSearch.trim()
    if (query.length < 2) {
      setSearchSuggestions([])
      setSearchDropdownOpen(false)
      return
    }
    const timer = window.setTimeout(() => {
      api<CaseRecord[]>(`/api/cases?search=${encodeURIComponent(query)}&includeClosed=true`)
        .then((matches) => {
          setSearchSuggestions(matches.slice(0, 8))
          setSearchDropdownOpen(true)
        })
        .catch(() => setSearchSuggestions([]))
    }, 250)
    return () => window.clearTimeout(timer)
  }, [topbarSearch])

  useEffect(() => {
    const media = window.matchMedia?.('(prefers-color-scheme: dark)')
    const applyTheme = () => {
      const resolved = theme === 'system' ? (media?.matches ? 'dark' : 'light') : theme
      document.documentElement.dataset.theme = resolved
    }
    applyTheme()
    media?.addEventListener?.('change', applyTheme)
    document.title = 'ARDOT Legal Division Case Planner'
    window.localStorage.setItem(themeStorageKey, theme)
    return () => media?.removeEventListener?.('change', applyTheme)
  }, [theme])

  useEffect(() => {
    if (selectedCaseId && casesView === 'workspace') {
      void loadWorkspace(selectedCaseId)
    }
  }, [selectedCaseId, casesView])

  useEffect(() => {
    if (page === 'dashboard') {
      void loadAttorneyDashboard(attorneyDashboardFilters)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, attorneyDashboardFilters])

  useEffect(() => {
    const caseId = selectedCaseId
    if (caseTab === 'riskAnalysis' && caseId && riskAnalysisLoadedForCase !== caseId) {
      setRiskAnalysisLoadedForCase(caseId)
      Promise.all([
        api<RiskAnalysisResult>(`/api/cases/${caseId}/risk-analysis`),
        api<ValuationPosition[]>(`/api/cases/${caseId}/valuation-positions`),
        api<ComparableSale[]>(`/api/cases/${caseId}/comparable-sales`),
        api<RiskAnalysisOfferLogEntry[]>(`/api/cases/${caseId}/risk-analysis-offers`),
        api<RiskAnalysisHistoryRecord[]>(`/api/cases/${caseId}/risk-analysis/history`),
      ])
        .then(([result, positions, sales, offers, history]) => {
          const landownerPosition = positions.find((p) => p.side === 'Landowner')
          let rows: RiskAnalysisRowInput[]
          let autoFilled = false

          if (result.rows.some((r) => !r.isSplit)) {
            rows = result.rows.filter((r) => !r.isSplit).map((r) => ({
              rowKey: r.rowKey, label: r.label, offerMaker: r.offerMaker, includeSplit: r.includeSplit,
              justCompensation: r.justCompensation, landownerFeesCosts: r.landownerFeesCosts, ashcCosts: r.ashcCosts, hourlyFeesRisk: r.hourlyFeesRisk,
            }))
          } else {
            // Fresh case with no saved ledger yet - seed the fixed 5-row starting point and
            // auto-fill the landowner appraisal row from the Valuation Position tab if it's on file.
            rows = defaultRiskAnalysisRows()
            if (landownerPosition?.appraisedValue != null) {
              const appraisalRow = rows.find((r) => r.rowKey === 'LandownerAppraisal')
              if (appraisalRow) {
                appraisalRow.justCompensation = landownerPosition.appraisedValue
                autoFilled = true
              }
            }
          }

          setRiskAnalysisPreview(result)
          setRiskAnalysisEditorOpen(false)
          setRiskAnalysisDate(result.analysisDate || new Date().toISOString().slice(0, 10))
          setRiskAnalysisInterestRate(result.interestRate || 0.06)
          setRiskAnalysisContingencyPercent(result.contingencyFeePercent || 0.30)
          setRiskAnalysisNarrative(result.narrative ?? '')
          setRiskAnalysisRows(rows)
          setValuationPositions(positions)
          setComparableSales(sales)
          setOfferLog(offers)
          setRiskAnalysisHistory(history)
          setValuationDrafts({
            ASHC: positions.find((p) => p.side === 'ASHC') ?? emptyValuationPosition(caseId, 'ASHC'),
            Landowner: landownerPosition ?? emptyValuationPosition(caseId, 'Landowner'),
          })
          if (autoFilled) void recomputeRiskAnalysis(rows, result.narrative ?? '')
        })
        .catch((error) => setErrorMessage(error instanceof Error ? error.message : 'Unable to load Valuation & Risk data.'))
    }
  }, [caseTab, selectedCaseId, riskAnalysisLoadedForCase])

  useEffect(() => {
    const caseId = selectedCaseId
    if (caseTab === 'overview' && caseId && activityLogLoadedForCase !== caseId) {
      setActivityLogLoadedForCase(caseId)
      api<ActivityLogEntry[]>(`/api/cases/${caseId}/activity`)
        .then((entries) => setActivityLog(entries))
        .catch((error) => setErrorMessage(error instanceof Error ? error.message : 'Unable to load recent activity.'))
    }
  }, [caseTab, selectedCaseId, activityLogLoadedForCase])

  useEffect(() => {
    const caseId = selectedCaseId
    if (caseTab === 'discovery' && caseId && discoveryPostureLoadedForCase !== caseId) {
      setDiscoveryPostureLoadedForCase(caseId)
      api<DiscoveryPosture | null>(`/api/cases/${caseId}/discovery-posture`)
        .then((posture) => setDiscoveryPosture(posture ?? emptyDiscoveryPosture(caseId)))
        .catch((error) => setErrorMessage(error instanceof Error ? error.message : 'Unable to load discovery strategy.'))
    }
  }, [caseTab, selectedCaseId, discoveryPostureLoadedForCase])

  useEffect(() => {
    const caseId = selectedCaseId
    if (caseTab === 'trialNotebook' && caseId && trialNotebookLoadedForCase !== caseId) {
      setTrialNotebookLoadedForCase(caseId)
      Promise.all([
        api<Witness[]>(`/api/cases/${caseId}/witnesses`),
        api<Exhibit[]>(`/api/cases/${caseId}/exhibits`),
        api<TrialMotion[]>(`/api/cases/${caseId}/trial-motions`),
      ])
        .then(([loadedWitnesses, loadedExhibits, loadedMotions]) => {
          setWitnesses(loadedWitnesses)
          setExhibits(loadedExhibits)
          setTrialMotions(loadedMotions)
        })
        .catch((error) => setErrorMessage(error instanceof Error ? error.message : 'Unable to load trial notebook data.'))
    }
  }, [caseTab, selectedCaseId, trialNotebookLoadedForCase])

  useEffect(() => {
    if (!activeModal) return
    function handleEscape(event: KeyboardEvent) {
      if (event.key !== 'Escape') return
      if (modalDirty) {
        setMessage('Save your changes or use Cancel to discard them before closing.')
        return
      }
      setActiveModal(null)
      setModalDirty(false)
      setErrorMessage('')
    }
    window.addEventListener('keydown', handleEscape)
    return () => window.removeEventListener('keydown', handleEscape)
  }, [activeModal, modalDirty])

  useEffect(() => {
    if (!caseMenuOpen) return
    function handleEscape(event: KeyboardEvent) {
      if (event.key === 'Escape') setCaseMenuOpen(false)
    }
    function handleOutsideClick(event: MouseEvent) {
      if (caseMenuRef.current && !caseMenuRef.current.contains(event.target as Node)) setCaseMenuOpen(false)
    }
    window.addEventListener('keydown', handleEscape)
    window.addEventListener('mousedown', handleOutsideClick)
    return () => {
      window.removeEventListener('keydown', handleEscape)
      window.removeEventListener('mousedown', handleOutsideClick)
    }
  }, [caseMenuOpen])

  async function loadInitial() {
    try {
      setErrorMessage('')
      const [dashboardData, caseList, allCaseList, diagnosticsData, deadlinesData, checklistData, discoveryData, serviceData, hearingsData, orgDefaultsData, templateTagsData, checklistTemplatesData, deadlineTemplatesData, issueTagsData, backupsData, referenceLibraryData] = await Promise.all([
        api<DashboardData>('/api/dashboard'),
        api<CaseRecord[]>(`/api/cases?search=${encodeURIComponent(caseSearch)}&status=${encodeURIComponent(statusFilter)}&caseStatus=${encodeURIComponent(caseStatusFilter)}&county=${encodeURIComponent(countyFilter)}&includeClosed=${includeClosed}`),
        api<CaseRecord[]>('/api/cases?includeClosed=true'),
        api<DiagnosticsSnapshot>('/api/diagnostics'),
        api<DeadlineItem[]>('/api/work-queues/deadlines'),
        api<ChecklistItem[]>('/api/work-queues/checklist'),
        api<DiscoveryItem[]>('/api/work-queues/discovery'),
        api<ServiceQueueItem[]>('/api/work-queues/service'),
        api<Hearing[]>('/api/work-queues/hearings'),
        api<OrgDefaults>('/api/org-defaults'),
        api<TemplateTag[]>('/api/template-tags'),
        api<ChecklistTemplate[]>('/api/checklist-templates'),
        api<DeadlineTemplate[]>('/api/deadline-templates'),
        api<IssueTag[]>('/api/issue-tags'),
        api<BackupInfo[]>('/api/backups'),
        api<ReferenceDocument[]>('/api/reference-library'),
      ])
      setDashboard(dashboardData)
      setCases(caseList)
      setAllCases(allCaseList)
      setDiagnostics(diagnosticsData)
      setQueueDeadlines(deadlinesData)
      setQueueChecklist(checklistData)
      setQueueDiscovery(discoveryData)
      setQueueService(serviceData)
      setQueueHearings(hearingsData)
      setOrgDefaults(orgDefaultsData)
      setTemplateTags(templateTagsData)
      setChecklistTemplates(checklistTemplatesData)
      setDeadlineTemplates(deadlineTemplatesData)
      setAllIssueTags(issueTagsData)
      setBackups(backupsData)
      setReferenceLibrary(referenceLibraryData)
      setMessage('Local workspace ready.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to load the local app.')
    }
  }

  async function loadCases() {
    try {
      setErrorMessage('')
      const caseList = await api<CaseRecord[]>(`/api/cases?search=${encodeURIComponent(caseSearch)}&status=${encodeURIComponent(statusFilter)}&caseStatus=${encodeURIComponent(caseStatusFilter)}&county=${encodeURIComponent(countyFilter)}&includeClosed=${includeClosed}`)
      setCases(caseList)
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to load cases.')
    }
  }

  // Auto-apply handlers (stage chips, track/county selects, include-closed toggle) pass the
  // just-changed value explicitly rather than reading state, since the corresponding setState
  // call hasn't re-rendered yet when this runs in the same event handler.
  async function loadCasesWithOverride(overrides: Partial<{ search: string; stage: string; track: string; county: string; includeClosed: boolean; status: string; caseStatus: string }>) {
    try {
      setErrorMessage('')
      const search = overrides.search ?? caseSearch
      const county = overrides.county ?? countyFilter
      const closed = overrides.includeClosed ?? includeClosed
      const status = overrides.status ?? statusFilter
      const consolidated = overrides.caseStatus ?? caseStatusFilter
      const caseList = await api<CaseRecord[]>(`/api/cases?search=${encodeURIComponent(search)}&status=${encodeURIComponent(status)}&caseStatus=${encodeURIComponent(consolidated)}&county=${encodeURIComponent(county)}&includeClosed=${closed}`)
      setCases(caseList)
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to load cases.')
    }
  }

  function toggleCaseSort(column: CaseSortColumn) {
    if (caseSortColumn === column) {
      setCaseSortDirection((current) => (current === 'asc' ? 'desc' : 'asc'))
    } else {
      setCaseSortColumn(column)
      setCaseSortDirection('asc')
    }
  }

  async function loadPlatformGenerationHistory(caseId: number) {
    try {
      setPlatformGenerationHistory(await api<DocumentGenerationHistoryItem[]>(`/api/cases/${caseId}/document-platform/generations`))
    } catch {
      setPlatformGenerationHistory([])
    }
  }

  async function loadWorkspace(caseId: number) {
    try {
      setErrorMessage('')
      const data = await api<WorkspaceResponse>(`/api/cases/${caseId}`)
      setWorkspace(data)
      setCaseDraft(data.case)
      void loadPlatformGenerationHistory(caseId)
      setDeadlineDraft(emptyDeadline(caseId))
      setChecklistDraft(emptyChecklist(caseId))
      setDiscoveryDraft(emptyDiscovery(caseId))
      setPublicationDraft(data.publication ?? emptyPublication(caseId))
      setServiceLogEntries(data.serviceLogEntries ?? [])
      setServiceLogDraft(emptyServiceLogEntry(caseId))
      setServiceLogFormOpen(false)
      setPublicationEntries(data.publicationEntries ?? [])
      setPublicationEntryDraft(emptyPublicationEntry(caseId))
      setPublicationEntryFormOpen(false)
      setNoteDraft({ id: 0, caseId, title: '', body: '', createdAt: '', updatedAt: '' })
      setHearingDraft({ id: 0, caseId, title: '', hearingDate: '', location: '', description: '', createdAt: '', updatedAt: '' })
      setSelectedTagId(0)
      setIssueTagMessage('')
      setShowAllCaseRecordFields(false)
      setSelectedDeadlineIds([])
      setSelectedChecklistIds([])
      setBulkDeadlineDueDate('')
      setBulkChecklistDueDate('')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to load the selected case.')
    }
  }

  async function refreshAll(caseId?: number | null) {
    await loadInitial()
    if (caseId) {
      await loadWorkspace(caseId)
    }
  }

  function clearModalFeedback() {
    setModalErrorSummary('')
    setModalFieldErrors({})
  }

  function openSettingsSection(section: SettingsSectionKey) {
    setSettingsSection(section)
    setPage('settings')
  }

  function setModalFeedback(summary: string, fieldErrors: FieldErrors = {}) {
    setModalErrorSummary(summary)
    setModalFieldErrors(fieldErrors)
  }

  function resetModalDraft(kind: ModalKind, mode: ModalMode = 'create') {
    const caseId = selectedCaseId ?? caseDraft.id
    if (kind === 'case') {
      setCaseDraft(mode === 'edit' && workspace ? workspace.case : emptyCase())
    } else if (kind === 'deadline') {
      setDeadlineDraft(mode === 'edit' ? deadlineDraft : emptyDeadline(caseId))
    } else if (kind === 'checklist') {
      setChecklistDraft(mode === 'edit' ? checklistDraft : emptyChecklist(caseId))
    } else if (kind === 'discovery') {
      setDiscoveryDraft(mode === 'edit' ? discoveryDraft : emptyDiscovery(caseId))
    } else if (kind === 'comparableSale') {
      setComparableSaleDraft(mode === 'edit' ? comparableSaleDraft : emptyComparableSale(caseId, comparableSaleDraft.side))
    } else if (kind === 'witness') {
      setWitnessDraft(mode === 'edit' ? witnessDraft : emptyWitness(caseId))
    } else if (kind === 'exhibit') {
      setExhibitDraft(mode === 'edit' ? exhibitDraft : emptyExhibit(caseId))
    } else if (kind === 'trialMotion') {
      setTrialMotionDraft(mode === 'edit' ? trialMotionDraft : emptyTrialMotion(caseId))
    } else if (kind === 'event') {
      setHearingDraft(mode === 'edit' ? hearingDraft : emptyHearing(caseId))
    }
  }

  function openModal(kind: ModalKind, mode: ModalMode) {
    setModalMode(mode)
    setActiveModal(kind)
    setModalDirty(false)
    setErrorMessage('')
    clearModalFeedback()
    if (mode === 'create') {
      resetModalDraft(kind, mode)
    }
  }

  function cancelModal() {
    const kind = activeModal
    setActiveModal(null)
    setModalDirty(false)
    setErrorMessage('')
    clearModalFeedback()
    if (kind === 'case') {
      setCaseDraft(workspace?.case ?? emptyCase())
    } else if (kind === 'deadline') {
      setDeadlineDraft(emptyDeadline(selectedCaseId ?? caseDraft.id))
    } else if (kind === 'checklist') {
      setChecklistDraft(emptyChecklist(selectedCaseId ?? caseDraft.id))
    } else if (kind === 'discovery') {
      setDiscoveryDraft(emptyDiscovery(selectedCaseId ?? caseDraft.id))
    } else if (kind === 'comparableSale') {
      setComparableSaleDraft(emptyComparableSale(selectedCaseId ?? caseDraft.id, comparableSaleDraft.side))
    } else if (kind === 'witness') {
      setWitnessDraft(emptyWitness(selectedCaseId ?? caseDraft.id))
    } else if (kind === 'exhibit') {
      setExhibitDraft(emptyExhibit(selectedCaseId ?? caseDraft.id))
    } else if (kind === 'trialMotion') {
      setTrialMotionDraft(emptyTrialMotion(selectedCaseId ?? caseDraft.id))
    } else if (kind === 'event') {
      setHearingDraft(emptyHearing(selectedCaseId ?? caseDraft.id))
    }
  }

  function patchCaseDraft(patch: Partial<CaseRecord>) {
    setCaseDraft((current) => ({ ...current, ...patch }))
    clearModalFeedback()
    setModalDirty(true)
  }

  function scrollToCaseEditorSection(key: CaseEditorSectionKey) {
    const node = caseEditorSectionRefs.current[key]
    if (!node) return
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    node.scrollIntoView({ behavior: reduceMotion ? 'auto' : 'smooth', block: 'start' })
  }

  function patchDeadlineDraft(patch: Partial<DeadlineItem>) {
    setDeadlineDraft((current) => ({ ...current, ...patch }))
    clearModalFeedback()
    setModalDirty(true)
  }

  function patchChecklistDraft(patch: Partial<ChecklistItem>) {
    setChecklistDraft((current) => ({ ...current, ...patch }))
    clearModalFeedback()
    setModalDirty(true)
  }

  function patchDiscoveryDraft(patch: Partial<DiscoveryItem>) {
    setDiscoveryDraft((current) => ({ ...current, ...patch }))
    clearModalFeedback()
    setModalDirty(true)
  }

  function goToCaseList() {
    setCasesView('list')
    setCaseTab('overview')
    setWorkspace(null)
    setSelectedCaseId(null)
    setCaseDraft(emptyCase())
    setDeadlineDraft(emptyDeadline())
    setChecklistDraft(emptyChecklist())
    setDiscoveryDraft(emptyDiscovery())
    setPublicationDraft(emptyPublication())
    setIssueTagMessage('')
    setErrorMessage('')
  }

  function submitGlobalSearch(event: FormEvent) {
    event.preventDefault()
    const query = topbarSearch
    setPage('cases')
    setCasesView('list')
    setCaseSearch(query)
    void loadCasesWithOverride({ search: query })
  }

  function goToTriageQueue() {
    setPage('cases')
    goToCaseList()
    setStatusFilter('Triage')
    void loadCasesWithOverride({ status: 'Triage' })
  }

  function startNewCase() {
    setPage('cases')
    setCasesView('list')
    setSelectedCaseId(null)
    setWorkspace(null)
    setCaseDraft(emptyCase())
    setCaseTab('overview')
    setDeadlineDraft(emptyDeadline())
    setChecklistDraft(emptyChecklist())
    setDiscoveryDraft(emptyDiscovery())
    setPublicationDraft(emptyPublication())
    setIssueTagMessage('')
    setErrorMessage('')
    openModal('case', 'create')
  }

  function openCase(caseId: number, nextTab: CaseTabKey) {
    setPage('cases')
    setCasesView('workspace')
    setSelectedCaseId(caseId)
    setCaseTab(nextTab)
    setSelectedDeadlineIds([])
    setSelectedChecklistIds([])
    setBulkDeadlineDueDate('')
    setBulkChecklistDueDate('')
  }

  async function loadAttorneyDashboard(filters: AttorneyDashboardFilters) {
    setAttorneyDashboardLoading(true)
    setAttorneyDashboardError('')
    try {
      const params = new URLSearchParams()
      if (filters.matterType) params.set('matterType', filters.matterType)
      if (filters.project) params.set('project', filters.project)
      if (filters.county) params.set('county', filters.county)
      if (filters.priority) params.set('priority', filters.priority)
      if (filters.currentHolder) params.set('currentHolder', filters.currentHolder)
      if (filters.stage) params.set('stage', filters.stage)
      if (filters.trialTrack !== undefined) params.set('trialTrack', String(filters.trialTrack))
      if (filters.momentumStatus) params.set('momentumStatus', filters.momentumStatus)
      if (filters.search) params.set('search', filters.search)
      const query = params.toString()
      const data = await api<AttorneyDashboardResponse>(`/api/dashboard/attorney${query ? `?${query}` : ''}`)
      setAttorneyDashboard(data)
    } catch (error) {
      setAttorneyDashboardError(error instanceof Error ? error.message : 'Unable to load the dashboard.')
    } finally {
      setAttorneyDashboardLoading(false)
    }
  }

  async function refreshAttorneyDashboard() {
    await loadAttorneyDashboard(attorneyDashboardFilters)
  }

  async function saveReferenceDocument() {
    if (!referenceEditDraft) return
    try {
      const saved = await api<ReferenceDocument>('/api/reference-library/' + encodeURIComponent(referenceEditDraft.key), { method: 'PUT', body: JSON.stringify(referenceEditDraft) })
      setReferenceLibrary((items) => items.some((item) => item.key === saved.key) ? items.map((item) => item.key === saved.key ? saved : item) : [...items, saved].sort((a, b) => a.title.localeCompare(b.title)))
      setReferenceEditKey(null)
      setReferenceEditDraft(null)
      setMessage('Reference document saved.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to save reference document.')
    }
  }

  async function deleteReferenceDocument(key: string) {
    if (!window.confirm('Remove this reference document?')) return
    try {
      await api('/api/reference-library/' + encodeURIComponent(key), { method: 'DELETE' })
      setReferenceLibrary((items) => items.filter((item) => item.key !== key))
      setMessage('Reference document removed.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to remove reference document.')
    }
  }

  function applyCaseRowVersion(caseId: number, rowVersion?: string | null) {
    if (!rowVersion) return
    const patch = (record: CaseRecord) => record.id === caseId ? { ...record, rowVersion } : record
    setAllCases((current) => current.map(patch))
    setCases((current) => current.map(patch))
    setWorkspace((current) => current?.case.id === caseId ? { ...current, case: { ...current.case, rowVersion } } : current)
  }

  const actionQueueHandlers: ActionQueueHandlers = {
    onOpenCase: (caseId) => openCase(caseId, 'overview'),
    onSetDiscoveryStrategy: async (caseId, strategy) => {
      try {
        const posture = await api<DiscoveryPosture>(`/api/cases/${caseId}/discovery-posture`)
        await api(`/api/cases/${caseId}/discovery-posture`, { method: 'POST', body: JSON.stringify({ ...posture, strategy }) })
        await refreshAttorneyDashboard()
        setMessage('Discovery strategy saved from the action queue.')
      } catch (error) { setErrorMessage(error instanceof Error ? error.message : 'Unable to save discovery strategy.') }
    },
    onCompleteDeadline: async (caseId, deadlineId) => {
      try {
        const deadlines = await api<DeadlineItem[]>(`/api/cases/${caseId}/deadlines`)
        const deadline = deadlines.find((item) => item.id === deadlineId)
        if (!deadline) throw new Error('Deadline not found.')
        await api('/api/deadlines', { method: 'POST', body: JSON.stringify({ ...deadline, status: 'Done', completedAt: new Date().toISOString() }) })
        await refreshAttorneyDashboard()
        setMessage('Deadline marked complete from the action queue.')
      } catch (error) { setErrorMessage(error instanceof Error ? error.message : 'Unable to complete the deadline.') }
    },
    onUpdateDeadline: async (caseId, deadlineId, dueDate, reason) => {
      try {
        const deadlines = await api<DeadlineItem[]>(`/api/cases/${caseId}/deadlines`)
        const deadline = deadlines.find((item) => item.id === deadlineId)
        if (!deadline) throw new Error('Deadline not found.')
        await api('/api/deadlines', { method: 'POST', body: JSON.stringify({ ...deadline, dueDate, reasonForChange: reason }) })
        await refreshAttorneyDashboard()
        setMessage('Deadline updated from the action queue.')
      } catch (error) { setErrorMessage(error instanceof Error ? error.message : 'Unable to update the deadline.') }
    },
    onOpenDiscovery: (caseId) => openCase(caseId, 'discovery'),
    onSetNextAction: async (caseId, nextAction, reviewDate) => {
      try {
        const result = await api<{ rowVersion?: string | null }>(`/api/cases/${caseId}/next-action`, { method: 'POST', body: JSON.stringify({ rowVersion: allCases.find((item) => item.id === caseId)?.rowVersion, nextAction, nextReviewDate: reviewDate }) })
        applyCaseRowVersion(caseId, result.rowVersion)
        await refreshAttorneyDashboard()
        setMessage('Next action updated.')
      } catch (error) {
        setErrorMessage(error instanceof Error ? error.message : 'Unable to update the next action.')
      }
    },
    onMarkWaiting: async (caseId, waitingOnValue, expectedResponseValue, followUpDateValue) => {
      try {
        const result = await api<{ rowVersion?: string | null }>(`/api/cases/${caseId}/waiting`, {
          method: 'POST',
          body: JSON.stringify({ rowVersion: allCases.find((item) => item.id === caseId)?.rowVersion, waitingOn: waitingOnValue, expectedResponse: expectedResponseValue, waitingFollowUpDate: followUpDateValue }),
        })
        applyCaseRowVersion(caseId, result.rowVersion)
        await refreshAttorneyDashboard()
        setMessage('Waiting condition recorded.')
      } catch (error) {
        setErrorMessage(error instanceof Error ? error.message : 'Unable to record the waiting condition.')
      }
    },
    onAddNote: async (caseId, note) => {
      try {
        const saved = await api<{ id: number }>(`/api/case-notes`, { method: 'POST', body: JSON.stringify({ caseId, title: 'Attorney Action Queue Note', body: note }) })
        await api(`/api/cases/${caseId}/activity`, { method: 'POST', body: JSON.stringify({ activityType: 'CaseNoteAdded', notes: `Case note added from Attorney Action Queue (note ${saved.id}).` }) })
        await refreshAttorneyDashboard()
        setMessage('Note saved.')
      } catch (error) {
        setErrorMessage(error instanceof Error ? error.message : 'Unable to save the note.')
      }
    },
    onDefer: async (caseId, reason, futureReviewDate) => {
      try {
        const result = await api<{ rowVersion?: string | null }>(`/api/cases/${caseId}/defer`, { method: 'POST', body: JSON.stringify({ rowVersion: allCases.find((item) => item.id === caseId)?.rowVersion, reason, futureReviewDate }) })
        applyCaseRowVersion(caseId, result.rowVersion)
        await refreshAttorneyDashboard()
        setMessage('Deferred.')
      } catch (error) {
        setErrorMessage(error instanceof Error ? error.message : 'Unable to defer this item.')
      }
    },
    onAssignHolder: async (caseId, holder) => {
      try {
        const result = await api<{ rowVersion?: string | null }>(`/api/cases/${caseId}/holder`, { method: 'POST', body: JSON.stringify({ rowVersion: allCases.find((item) => item.id === caseId)?.rowVersion, currentHolder: holder }) })
        applyCaseRowVersion(caseId, result.rowVersion)
        await refreshAttorneyDashboard()
        setMessage('Holder updated.')
      } catch (error) {
        setErrorMessage(error instanceof Error ? error.message : 'Unable to update the holder.')
      }
    },
  }

  async function submitBulkDefer() {
    if (selectedActionQueueIds.length === 0) return
    const futureReviewDate = bulkDeferDate || (() => {
      const date = new Date()
      date.setDate(date.getDate() + 7)
      return date.toISOString().slice(0, 10)
    })()
    try {
      const result = await api<{ rowVersions?: Record<string, string | null> }>('/api/cases/bulk-defer', {
        method: 'POST',
        body: JSON.stringify({
          caseIds: selectedActionQueueIds,
          rowVersions: Object.fromEntries(selectedActionQueueIds.map((id) => [id, allCases.find((item) => item.id === id)?.rowVersion || ''])),
          reason: '',
          futureReviewDate,
        }),
      })
      Object.entries(result.rowVersions || {}).forEach(([id, version]) => applyCaseRowVersion(Number(id), version))
      setBulkDeferOpen(false)
      setBulkDeferDate('')
      setSelectedActionQueueIds([])
      await refreshAttorneyDashboard()
      setMessage('Selected cases deferred.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to defer the selected cases.')
    }
  }

  function openHandoffDialog(caseId: number) {
    const row = attorneyDashboard?.filingPipeline.allPipeline.find((r) => r.caseId === caseId)
    setHandoffTarget({ caseId, caseName: row?.tractOrOwnerName ?? `Case ${caseId}` })
  }

  async function submitHandoff(payload: { newHolder: string; newStage: string; handoffDate: string; nextReviewDate: string; note: string }) {
    if (!handoffTarget) return
    try {
      const saved = await api<PipelineHandoffRecord>(`/api/cases/${handoffTarget.caseId}/pipeline-handoff`, {
        method: 'POST',
        body: JSON.stringify({ ...payload, rowVersion: allCases.find((item) => item.id === handoffTarget.caseId)?.rowVersion }),
      })
      applyCaseRowVersion(handoffTarget.caseId, saved.caseRowVersion)
      setHandoffTarget(null)
      await refreshAttorneyDashboard()
      setMessage('Pipeline handoff recorded.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to record the handoff.')
    }
  }

  async function submitCaseRecordDecision(payload: { activityType: string; notes: string }) {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    try {
      await api(`/api/cases/${caseId}/activity`, { method: 'POST', body: JSON.stringify(payload) })
      const entries = await api<ActivityLogEntry[]>(`/api/cases/${caseId}/activity`)
      setActivityLog(entries)
      setCaseRecordDecisionOpen(false)
      setMessage('Decision recorded.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to record the decision.')
    }
  }

  async function saveActivityEdit() {
    setEditingActivityId(null)
  }

  function applyBulkDeferPreset(preset: '7' | '14' | '30' | 'custom') {
    setBulkDeferPreset(preset)
    if (preset === 'custom') return
    const date = new Date()
    date.setDate(date.getDate() + Number(preset))
    setBulkDeferDate(date.toISOString().slice(0, 10))
  }

  async function saveDiscoveryPosture() {
    const caseId = selectedCaseId
    if (!caseId || !discoveryPosture) return
    setDiscoveryPostureSaving(true)
    try {
      const saved = await api<DiscoveryPosture>(`/api/cases/${caseId}/discovery-posture`, { method: 'POST', body: JSON.stringify(discoveryPosture) })
      setDiscoveryPosture(saved)
      setDiscoveryStrategyEditing(false)
      setMessage('Discovery strategy saved.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to save discovery strategy.')
    } finally {
      setDiscoveryPostureSaving(false)
    }
  }

  function startEditCase() {
    if (!selectedCase.id) return
    setCaseDraft(selectedCase)
    openModal('case', 'edit')
  }

  function startNewCaseNote() {
    const caseId = selectedCaseId ?? selectedCase.id
    if (!caseId) return
    setNoteDraft({ id: 0, caseId, title: '', body: '', createdAt: '', updatedAt: '' })
  }

  function startEditCaseNote(note: CaseNote) {
    setNoteDraft(note)
  }

  function startNewHearing() {
    const caseId = selectedCaseId ?? selectedCase.id
    if (!caseId) return
    setHearingDraft(emptyHearing(caseId))
    openModal('event', 'create')
  }

  function startEditHearing(hearing: Hearing) {
    setHearingDraft(hearing)
    openModal('event', 'edit')
  }

  function startDeadlineModal(item?: DeadlineItem) {
    if (item) {
      setDeadlineDraft(item)
      openModal('deadline', 'edit')
      return
    }
    setDeadlineDraft(emptyDeadline(selectedCaseId ?? caseDraft.id))
    openModal('deadline', 'create')
  }

  function startChecklistModal(item?: ChecklistItem) {
    if (item) {
      setChecklistDraft(item)
      openModal('checklist', 'edit')
      return
    }
    setChecklistDraft(emptyChecklist(selectedCaseId ?? caseDraft.id))
    openModal('checklist', 'create')
  }

  function startDiscoveryModal(item?: DiscoveryItem) {
    if (item) {
      setDiscoveryDraft(item)
      openModal('discovery', 'edit')
      return
    }
    setDiscoveryDraft(emptyDiscovery(selectedCaseId ?? caseDraft.id))
    openModal('discovery', 'create')
  }

  function patchComparableSaleDraft(patch: Partial<ComparableSale>) {
    setComparableSaleDraft((current) => ({ ...current, ...patch }))
    clearModalFeedback()
    setModalDirty(true)
  }

  function startComparableSaleModal(side: ValuationSide, item?: ComparableSale) {
    if (item) {
      setComparableSaleDraft(item)
      openModal('comparableSale', 'edit')
      return
    }
    setComparableSaleDraft(emptyComparableSale(selectedCaseId ?? caseDraft.id, side))
    openModal('comparableSale', 'create')
  }

  function patchWitnessDraft(patch: Partial<Witness>) {
    setWitnessDraft((current) => ({ ...current, ...patch }))
    clearModalFeedback()
    setModalDirty(true)
  }

  function startWitnessModal(item?: Witness) {
    if (item) {
      setWitnessDraft(item)
      openModal('witness', 'edit')
      return
    }
    setWitnessDraft(emptyWitness(selectedCaseId ?? caseDraft.id))
    openModal('witness', 'create')
  }

  function patchExhibitDraft(patch: Partial<Exhibit>) {
    setExhibitDraft((current) => ({ ...current, ...patch }))
    clearModalFeedback()
    setModalDirty(true)
  }

  function startExhibitModal(item?: Exhibit) {
    if (item) {
      setExhibitDraft(item)
      openModal('exhibit', 'edit')
      return
    }
    setExhibitDraft(emptyExhibit(selectedCaseId ?? caseDraft.id))
    openModal('exhibit', 'create')
  }

  function patchTrialMotionDraft(patch: Partial<TrialMotion>) {
    setTrialMotionDraft((current) => ({ ...current, ...patch }))
    clearModalFeedback()
    setModalDirty(true)
  }

  function startTrialMotionModal(item?: TrialMotion) {
    if (item) {
      setTrialMotionDraft(item)
      openModal('trialMotion', 'edit')
      return
    }
    setTrialMotionDraft(emptyTrialMotion(selectedCaseId ?? caseDraft.id))
    openModal('trialMotion', 'create')
  }

  function serializeCaseDraft(draft: CaseRecord): CaseRecord {
    const consolidated = draft.caseStatus || 'Pipeline'
    const legacy = consolidated === 'Pipeline' ? { status: 'Pipeline', stage: '', track: draft.track || 'Contested' }
      : consolidated === 'Filed / Service Pending' ? { status: 'Active', stage: 'Service', track: draft.track || 'Contested' }
      : consolidated === 'Settlement Pending' ? { status: 'Active', stage: 'Discovery & Evaluation', track: 'Settlement' }
      : consolidated === 'Trial Preparation' ? { status: 'Active', stage: 'Trial Track', track: 'Contested' }
      : consolidated === 'Resolved / Closed' ? { status: 'Closed', stage: 'Resolved', track: draft.track || 'Contested' }
      : consolidated === 'Triage' ? { status: 'Triage', stage: draft.stage || '', track: draft.track || 'Contested' }
      : { status: 'Active', stage: 'Discovery & Evaluation', track: draft.track || 'Contested' }
    return {
      ...draft,
      caseNumber: draft.caseNumber.trim(),
      caseName: draft.caseName.trim(),
      jobNumber: draft.jobNumber.trim(),
      tract: draft.tract.trim(),
      county: draft.county.trim(),
      ...legacy,
      caseStatus: consolidated,
      filingDate: normalizeDateValue(draft.filingDate),
      dateOfTaking: normalizeDateValue(draft.dateOfTaking),
      trialDate: normalizeDateValue(draft.trialDate),
      depositAmount: draft.depositAmount == null || Number.isNaN(draft.depositAmount) ? null : draft.depositAmount,
      owner: normalizeTextValue(draft.owner),
      landowner: normalizeTextValue(draft.landowner),
      valuationNotes: normalizeTextValue(draft.valuationNotes),
      settlementNotes: normalizeTextValue(draft.settlementNotes),
      publicationServiceNotes: normalizeTextValue(draft.publicationServiceNotes),
      servicePerfectedDate: normalizeDateValue(draft.servicePerfectedDate),
      serviceDeadline120: normalizeDateValue(draft.serviceDeadline120),
      serviceDeadlineBasisDate: normalizeDateValue(draft.serviceDeadlineBasisDate),
      serviceMethod: normalizeTextValue(draft.serviceMethod),
      serviceNotes: normalizeTextValue(draft.serviceNotes),
      serviceStatus: normalizeTextValue(draft.serviceStatus),
      assignedAttorney: normalizeTextValue(draft.assignedAttorney),
      opposingCounsel: normalizeTextValue(draft.opposingCounsel),
      appraiser: normalizeTextValue(draft.appraiser),
      taxesOwed: normalizeTextValue(draft.taxesOwed),
      fundsWithdrawn: normalizeTextValue(draft.fundsWithdrawn),
      fundsWithdrawnDate: normalizeDateValue(draft.fundsWithdrawnDate),
      discoveryCompleted: normalizeTextValue(draft.discoveryCompleted),
      updatedAppraisal: normalizeTextValue(draft.updatedAppraisal),
      closedDate: normalizeDateValue(draft.closedDate),
    }
  }

  function serializeDeadlineDraft(draft: DeadlineItem, caseId: number): DeadlineItem {
    return {
      ...draft,
      caseId,
      title: draft.title.trim(),
      dueDate: normalizeDateValue(draft.dueDate),
      status: draft.status.trim() || 'Open',
      notes: normalizeTextValue(draft.notes),
      sourceType: draft.sourceType.trim() || 'Manual',
      severity: draft.severity || 'normal',
      reasonForChange: normalizeTextValue(draft.reasonForChange),
    }
  }

  function serializeChecklistDraft(draft: ChecklistItem, caseId: number): ChecklistItem {
    return {
      ...draft,
      caseId,
      phase: draft.phase.trim() || 'General',
      task: draft.task.trim(),
      dueDate: normalizeDateValue(draft.dueDate),
      status: draft.status.trim() || 'Not Started',
      notes: normalizeTextValue(draft.notes),
      sourceType: draft.sourceType.trim() || 'Manual',
    }
  }

  function serializeDiscoveryDraft(draft: DiscoveryItem, caseId: number): DiscoveryItem {
    return {
      ...draft,
      caseId,
      direction: draft.direction.trim() || 'Served by Us',
      discoveryType: draft.discoveryType.trim(),
      servedDate: normalizeDateValue(draft.servedDate),
      dueDate: normalizeDateValue(draft.dueDate),
      responseDate: normalizeDateValue(draft.responseDate),
      followUpDate: normalizeDateValue(draft.followUpDate),
      status: draft.status.trim() || 'Waiting for Responses',
      assignedTo: normalizeTextValue(draft.assignedTo),
      notes: normalizeTextValue(draft.notes),
      goodFaithSentDate: normalizeDateValue(draft.goodFaithSentDate),
      motionToCompelDate: normalizeDateValue(draft.motionToCompelDate),
    }
  }

  async function deleteChecklistItem(item: ChecklistItem) {
    if (!window.confirm(`Delete task "${item.task}"?`)) return
    try {
      setErrorMessage('')
      await api(`/api/checklist/${item.id}`, { method: 'DELETE' })
      await refreshAll(item.caseId)
      setMessage('Task deleted.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to delete task.')
    }
  }

  async function deleteDeadline(item: DeadlineItem) {
    if (!window.confirm(`Delete deadline "${item.title}"?`)) return
    try {
      setErrorMessage('')
      await api(`/api/deadlines/${item.id}`, { method: 'DELETE' })
      await refreshAll(item.caseId)
      setMessage('Deadline deleted.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to delete deadline.')
    }
  }


  async function activateTriageCase() {
    const record = workspace?.case ?? selectedCase
    if (!record?.id) return
    try {
      setErrorMessage('')
      // Status must flip to Active BEFORE generation - GenerateDeadlines/ChecklistForCaseAsync
      // are gated to produce nothing for Triage cases.
      const workflowStatus = record.caseStatus && record.caseStatus !== 'Triage' ? record.caseStatus : 'Active Litigation'
      await api<CaseRecord>('/api/cases', { method: 'POST', body: JSON.stringify(serializeCaseDraft({ ...record, caseStatus: workflowStatus })) })
      await api(`/api/cases/${record.id}/activity`, { method: 'POST', body: JSON.stringify({ activityType: 'CaseActivated', notes: 'Triage completed; case activated' }) })
      setTriageWizardOpen(false)
      if (window.confirm('Generate checklist and deadline templates for this case now?')) {
        await addFromTemplates(record.id)
      } else {
        await refreshAll(record.id)
      }
      setMessage('Case activated. Live tracking has started.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to activate the case.')
    }
  }

  async function addFromTemplates(caseIdOverride?: number) {
    const caseId = caseIdOverride ?? selectedCaseId ?? caseDraft.id
    if (!caseId) return
    try {
      setErrorMessage('')
      const [checklistResult, deadlineResult] = await Promise.all([
        api<{ added: number }>(`/api/cases/${caseId}/generate-checklist`, { method: 'POST' }),
        api<{ added: number; updated: number }>(`/api/cases/${caseId}/generate-deadlines`, { method: 'POST' }),
      ])
      await refreshAll(caseId)
      setMessage(`From templates: ${checklistResult.added} task(s) added, ${deadlineResult.added} deadline(s) added, ${deadlineResult.updated} updated.`)
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to add from templates.')
    }
  }

  async function openWorkTemplatePicker(kind: 'Task' | 'Deadline' | 'All', caseIdOverride?: number) {
    const caseId = caseIdOverride ?? selectedCaseId ?? caseDraft.id
    if (!caseId) return
    try {
      const items = await api<WorkTemplateCandidate[]>(`/api/cases/${caseId}/work-template-candidates`)
      setWorkTemplatePicker({ caseId, kind, items: items.map((x) => ({ ...x, selected: !x.isDuplicate, allowDuplicate: false })) })
      setWorkTemplateFilter('recommended')
      setExpandedWorkTemplateId(null)
    } catch (error) { setErrorMessage(error instanceof Error ? error.message : 'Unable to load template candidates.') }
  }

  async function loadStatusMappingReview() {
    try {
      setStatusMappingReviewCases(await api<CaseRecord[]>('/api/case-status-mapping-review'))
    } catch (error) { setErrorMessage(error instanceof Error ? error.message : 'Unable to load status mapping review.') }
  }

  async function pipelineNote(caseId: number) {
    const note = window.prompt('Optional pipeline note')
    if (note === null) return
    try { const saved = await api<{ id: number }>('/api/case-notes', { method: 'POST', body: JSON.stringify({ caseId, title: 'Pipeline Note', body: note }) }); await api(`/api/cases/${caseId}/activity`, { method: 'POST', body: JSON.stringify({ activityType: 'CaseNoteAdded', notes: `Case note added from Pipeline (note ${saved.id}).` }) }); await refreshAttorneyDashboard(); setMessage('Pipeline note added.') }
    catch (error) { setErrorMessage(error instanceof Error ? error.message : 'Unable to add pipeline note.') }
  }

  async function pipelineHolder(caseId: number) {
    const holder = window.prompt('Current holder', 'Attorney')
    if (!holder) return
    try { const result = await api<{ rowVersion?: string | null }>(`/api/cases/${caseId}/holder`, { method: 'POST', body: JSON.stringify({ rowVersion: allCases.find((item) => item.id === caseId)?.rowVersion, currentHolder: holder }) }); applyCaseRowVersion(caseId, result.rowVersion); await refreshAttorneyDashboard(); setMessage('Pipeline holder updated.') }
    catch (error) { setErrorMessage(error instanceof Error ? error.message : 'Unable to update pipeline holder.') }
  }

  async function pipelineReview(caseId: number) {
    const date = window.prompt('Next review date (YYYY-MM-DD)', new Date().toISOString().slice(0, 10))?.trim()
    if (!date || !/^\d{4}-\d{2}-\d{2}$/.test(date)) return
    try { const result = await api<{ rowVersion?: string | null }>(`/api/cases/${caseId}/next-action`, { method: 'POST', body: JSON.stringify({ rowVersion: allCases.find((item) => item.id === caseId)?.rowVersion, nextAction: 'Review pipeline readiness', nextReviewDate: date }) }); applyCaseRowVersion(caseId, result.rowVersion); await refreshAttorneyDashboard(); setMessage('Pipeline review date updated.') }
    catch (error) { setErrorMessage(error instanceof Error ? error.message : 'Unable to set the Pipeline review date.') }
  }

  async function advancePipelineCase(caseId: number) {
    try {
      const workspace = await api<WorkspaceResponse>(`/api/cases/${caseId}`)
      const record = workspace.case
      if (!window.confirm(`Advance ${record.caseName} from Pipeline to Filed / Service Pending?`)) return
      const filingDate = window.prompt('Filing date (YYYY-MM-DD)', record.filingDate || new Date().toISOString().slice(0, 10))?.trim()
      if (!filingDate || !/^\d{4}-\d{2}-\d{2}$/.test(filingDate)) { setErrorMessage('A valid filing date is required to leave Pipeline.'); return }
      const saved = await api<CaseRecord>('/api/cases', { method: 'POST', body: JSON.stringify(serializeCaseDraft({ ...record, filingDate, caseStatus: 'Filed / Service Pending' })) })
      await api(`/api/cases/${caseId}/activity`, { method: 'POST', body: JSON.stringify({ activityType: 'CaseStatusChanged', notes: 'Advanced from Pipeline to Filed / Service Pending.' }) })
      const generationChoice = window.prompt('Generate applicable tasks and deadlines? Enter: all, review, or none', 'review')?.trim().toLowerCase()
      if (generationChoice === 'all') {
        await addFromTemplates(saved.id)
      } else if (generationChoice === 'review') {
        await refreshAll(saved.id)
        await openWorkTemplatePicker('All', saved.id)
      } else {
        await refreshAll(saved.id)
      }
      await refreshAttorneyDashboard(); setMessage(generationChoice === 'review' ? 'Pipeline case filed. Review proposed tasks and deadlines.' : 'Pipeline case filed.')
    } catch (error) { setErrorMessage(error instanceof Error ? error.message : 'Unable to advance pipeline case.') }
  }

  async function addReviewedWorkTemplates() {
    if (!workTemplatePicker) return
    const chosen = workTemplatePicker.items.filter((x) => x.selected && (workTemplatePicker.kind === 'All' || x.kind === workTemplatePicker.kind))
    try {
      const result = await api<{ added: number }>(`/api/cases/${workTemplatePicker.caseId}/work-template-selections`, { method: 'POST', body: JSON.stringify({ items: chosen.map((x) => ({ kind: x.kind, templateId: x.templateId, dueDate: x.dueDate, allowDuplicate: x.allowDuplicate })) }) })
      await refreshAll(workTemplatePicker.caseId); setWorkTemplatePicker(null); setMessage(`${result.added} reviewed template item(s) added.`)
    } catch (error) { setErrorMessage(error instanceof Error ? error.message : 'Unable to add reviewed templates.') }
  }

  async function toggleServicePerfected(perfected: boolean) {
    if (!selectedCase?.id) return
    try {
      const updated = await api<CaseRecord>('/api/cases', { method: 'POST', body: JSON.stringify({ ...selectedCase, servicePerfected: perfected, serviceStatus: perfected ? 'Perfected' : 'Pending' }) })
      await refreshAll(updated.id)
      await api(`/api/cases/${updated.id}/activity`, { method: 'POST', body: JSON.stringify({ activityType: perfected ? 'ServicePerfected' : 'ServiceUnperfected', notes: perfected ? 'Service marked perfected.' : 'Service marked not perfected.' }) })
      setMessage(perfected ? 'Service marked perfected.' : 'Service marked not perfected.')
    } catch (error) { setErrorMessage(error instanceof Error ? error.message : 'Unable to update service status.') }
  }

  async function changeStatus(newStatus: string) {
    const record = workspace?.case
    if (!record?.id || newStatus === (record.status || 'Active')) return
    if (newStatus === 'Closed' && !window.confirm('Mark this case Closed? It will drop off the main dashboard.')) return
    try {
      setErrorMessage('')
      let closedDate = record.closedDate
      if (newStatus === 'Closed' && !closedDate) {
        closedDate = window.prompt('Date Closed (YYYY-MM-DD). Leave blank to keep it unset.', new Date().toISOString().slice(0, 10))?.trim() || null
        if (closedDate && !/^\d{4}-\d{2}-\d{2}$/.test(closedDate)) { setErrorMessage('Date Closed must use YYYY-MM-DD.'); return }
      }
      if (newStatus !== 'Closed' && record.closedDate && window.confirm('Clear the active Date Closed value while reopening this case? The prior value remains in audit history.')) closedDate = null
      await api<CaseRecord>('/api/cases', { method: 'POST', body: JSON.stringify({ ...record, status: newStatus, closedDate }) })
      await refreshAll(record.id)
      setMessage(newStatus === 'Closed' ? 'Case marked Closed.' : 'Case reopened.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to change the case status.')
    }
  }

  async function regenerateTemplatesForPickedCase() {
    if (!templateRegenCaseId) return
    setTemplateRegenBusy(true)
    try {
      setErrorMessage('')
      const [checklistResult, deadlineResult] = await Promise.all([
        api<{ added: number }>(`/api/cases/${templateRegenCaseId}/generate-checklist`, { method: 'POST' }),
        api<{ added: number; updated: number }>(`/api/cases/${templateRegenCaseId}/generate-deadlines`, { method: 'POST' }),
      ])
      if (templateRegenCaseId === selectedCaseId) await refreshAll(templateRegenCaseId)
      setMessage(`Regenerated from templates: ${checklistResult.added} task(s) added, ${deadlineResult.added} deadline(s) added, ${deadlineResult.updated} updated.`)
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to regenerate from templates.')
    } finally {
      setTemplateRegenBusy(false)
    }
  }

  async function persistCasePatch(patch: Partial<CaseRecord>, successMessage: string) {
    const record = workspace?.case ?? selectedCase
    if (!record?.id) return
    const triageProgress = record.status === 'Triage' && !('status' in patch)
    const payload = {
      ...serializeCaseDraft({ ...record, ...patch }),
      // Triage may record the eventual consolidated status before activation, but it must
      // remain excluded from live queues and deadline generation until the final Activate step.
      ...(triageProgress ? { status: 'Triage' } : {}),
    }
    const validation = validateCaseDraft(payload)
    if (Object.keys(validation.fieldErrors).length > 0) {
      setErrorMessage(validation.summary)
      return
    }

    try {
      setErrorMessage('')
      await api<CaseRecord>('/api/cases', { method: 'POST', body: JSON.stringify(payload) })
      await refreshAll(record.id)
      setMessage(successMessage)
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to update the case.')
    }
  }

  async function persistDeadline(draft: DeadlineItem, successMessage: string, closeAfterSave: boolean) {
    const caseId = draft.caseId || selectedCaseId || caseDraft.id
    if (!caseId) return
    const payload = serializeDeadlineDraft(draft, caseId)
    const validation = validateDeadlineDraft(payload)
    if (Object.keys(validation.fieldErrors).length > 0) {
      setModalFeedback(validation.summary, validation.fieldErrors)
      return
    }

    try {
      setErrorMessage('')
      clearModalFeedback()
      await api<DeadlineItem>('/api/deadlines', { method: 'POST', body: JSON.stringify(payload) })
      await refreshAll(caseId)
      setDeadlineDraft(emptyDeadline(caseId))
      if (closeAfterSave) setActiveModal(null)
      setModalDirty(false)
      setMessage(successMessage)
    } catch (error) {
      const text = error instanceof Error ? error.message : 'Unable to save deadline.'
      setErrorMessage(text)
      setModalFeedback(text)
    }
  }

  async function persistChecklist(draft: ChecklistItem, successMessage: string, closeAfterSave: boolean) {
    const caseId = draft.caseId || selectedCaseId || caseDraft.id
    if (!caseId) return
    const payload = serializeChecklistDraft(draft, caseId)
    const validation = validateChecklistDraft(payload)
    if (Object.keys(validation.fieldErrors).length > 0) {
      setModalFeedback(validation.summary, validation.fieldErrors)
      return
    }

    try {
      setErrorMessage('')
      clearModalFeedback()
      await api<ChecklistItem>('/api/checklist', { method: 'POST', body: JSON.stringify(payload) })
      await refreshAll(caseId)
      setChecklistDraft(emptyChecklist(caseId))
      if (closeAfterSave) setActiveModal(null)
      setModalDirty(false)
      setMessage(successMessage)
    } catch (error) {
      const text = error instanceof Error ? error.message : 'Unable to save task.'
      setErrorMessage(text)
      setModalFeedback(text)
    }
  }

  async function markGlobalServicePerfected(caseId: number) {
    const caseRecord = dashboardCasesById.get(caseId)
    if (!caseRecord) return
    try {
      setErrorMessage('')
      const payload = serializeCaseDraft({ ...caseRecord, servicePerfected: true, servicePerfectedDate: caseRecord.servicePerfectedDate || new Date().toISOString().slice(0, 10) })
      await api<CaseRecord>('/api/cases', { method: 'POST', body: JSON.stringify(payload) })
      const refreshedService = await api<ServiceQueueItem[]>('/api/work-queues/service')
      setQueueService(refreshedService)
      setMessage('Service marked perfected.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to update service status.')
    }
  }

  function toggleSelectedDeadline(id: number) {
    setSelectedDeadlineIds((current) => (current.includes(id) ? current.filter((value) => value !== id) : [...current, id]))
  }

  function toggleSelectedChecklist(id: number) {
    setSelectedChecklistIds((current) => (current.includes(id) ? current.filter((value) => value !== id) : [...current, id]))
  }

  // Scoped merge/subtract (matches setPhaseSelectedChecklist below) rather than an overwrite, so
  // this can be used both for a "select all visible" control over a full list AND for a per-group
  // select-all in the Work tab without clobbering selections made in other groups.
  function setAllSelectedDeadlines(items: DeadlineItem[], checked: boolean) {
    const ids = items.map((item) => item.id)
    setSelectedDeadlineIds((current) => (checked ? Array.from(new Set([...current, ...ids])) : current.filter((id) => !ids.includes(id))))
  }

  function setAllSelectedChecklist(items: ChecklistItem[], checked: boolean) {
    setSelectedChecklistIds(checked ? items.map((item) => item.id) : [])
  }

  function setPhaseSelectedChecklist(phaseItems: ChecklistItem[], checked: boolean) {
    const phaseIds = phaseItems.map((item) => item.id)
    setSelectedChecklistIds((current) => {
      if (checked) {
        return Array.from(new Set([...current, ...phaseIds]))
      }
      return current.filter((id) => !phaseIds.includes(id))
    })
  }

  async function applyBulkDeadlineAction(action: 'complete' | 'reopen' | 'dueDate' | 'delete', items: DeadlineItem[]) {
    const selectedItems = items.filter((item) => selectedDeadlineIds.includes(item.id))
    if (selectedItems.length === 0) {
      setMessage('Select at least one deadline first.')
      return
    }

    if (action === 'dueDate' && !bulkDeadlineDueDate) {
      setMessage('Choose a due date before applying the bulk update.')
      return
    }

    if (action === 'delete' && !window.confirm(`Delete ${selectedItems.length} selected deadline${selectedItems.length === 1 ? '' : 's'}?`)) return

    try {
      setErrorMessage('')
      if (action === 'delete') {
        await Promise.all(selectedItems.map((item) => api(`/api/deadlines/${item.id}`, { method: 'DELETE' })))
      } else {
        await Promise.all(selectedItems.map((item) => {
          const updated: DeadlineItem = action === 'complete'
            ? { ...item, status: 'Done' }
            : action === 'reopen'
              ? { ...item, status: 'Reopened' }
              : { ...item, dueDate: bulkDeadlineDueDate }
          return api<DeadlineItem>('/api/deadlines', { method: 'POST', body: JSON.stringify(serializeDeadlineDraft(updated, item.caseId)) })
        }))
      }
      await refreshAll(selectedCaseId ?? undefined)
      setSelectedDeadlineIds([])
      setBulkDeadlineDueDate('')
      setMessage(
        action === 'delete'
          ? `${selectedItems.length} deadline${selectedItems.length === 1 ? '' : 's'} deleted.`
          : action === 'complete'
            ? `${selectedItems.length} deadline${selectedItems.length === 1 ? '' : 's'} marked done.`
            : action === 'reopen'
              ? `${selectedItems.length} deadline${selectedItems.length === 1 ? '' : 's'} reopened.`
              : `${selectedItems.length} deadline${selectedItems.length === 1 ? '' : 's'} updated to ${displayDate(bulkDeadlineDueDate)}.`,
      )
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to update the selected deadlines.')
    }
  }

  async function applyBulkChecklistAction(action: 'complete' | 'reopen' | 'dueDate' | 'delete', items: ChecklistItem[]) {
    const selectedItems = items.filter((item) => selectedChecklistIds.includes(item.id))
    if (selectedItems.length === 0) {
      setMessage('Select at least one task first.')
      return
    }

    if (action === 'dueDate' && !bulkChecklistDueDate) {
      setMessage('Choose a due date before applying the bulk update.')
      return
    }

    if (action === 'delete' && !window.confirm(`Delete ${selectedItems.length} selected task${selectedItems.length === 1 ? '' : 's'}?`)) return

    try {
      setErrorMessage('')
      if (action === 'delete') {
        await Promise.all(selectedItems.map((item) => api(`/api/checklist/${item.id}`, { method: 'DELETE' })))
      } else {
        await Promise.all(selectedItems.map((item) => {
          const updated: ChecklistItem = action === 'complete'
            ? { ...item, status: 'Done' }
            : action === 'reopen'
              ? { ...item, status: 'Reopened' }
              : { ...item, dueDate: bulkChecklistDueDate }
          return api<ChecklistItem>('/api/checklist', { method: 'POST', body: JSON.stringify(serializeChecklistDraft(updated, item.caseId)) })
        }))
      }
      await refreshAll(selectedCaseId ?? undefined)
      setSelectedChecklistIds([])
      setBulkChecklistDueDate('')
      setMessage(
        action === 'delete'
          ? `${selectedItems.length} task${selectedItems.length === 1 ? '' : 's'} deleted.`
          : action === 'complete'
            ? `${selectedItems.length} task${selectedItems.length === 1 ? '' : 's'} marked done.`
            : action === 'reopen'
              ? `${selectedItems.length} task${selectedItems.length === 1 ? '' : 's'} reopened.`
              : `${selectedItems.length} task${selectedItems.length === 1 ? '' : 's'} updated to ${displayDate(bulkChecklistDueDate)}.`,
      )
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to update the selected tasks.')
    }
  }

  async function persistDiscovery(draft: DiscoveryItem, successMessage: string, closeAfterSave: boolean) {
    // Match persistDeadline/persistChecklist: prefer the draft's own caseId so this also works
    // when called from a global context (e.g. the Work Queue) where no case is "selected".
    const caseId = draft.caseId || selectedCaseId || caseDraft.id
    if (!caseId) return
    const payload = serializeDiscoveryDraft(draft, caseId)
    const validation = validateDiscoveryDraft(payload)
    if (Object.keys(validation.fieldErrors).length > 0) {
      setModalFeedback(validation.summary, validation.fieldErrors)
      return
    }

    try {
      setErrorMessage('')
      clearModalFeedback()
      await api<DiscoveryItem>('/api/discovery', { method: 'POST', body: JSON.stringify(payload) })
      await refreshAll(caseId)
      setDiscoveryDraft(emptyDiscovery(caseId))
      if (closeAfterSave) setActiveModal(null)
      setModalDirty(false)
      setMessage(successMessage)
    } catch (error) {
      const text = error instanceof Error ? error.message : 'Unable to save discovery item.'
      setErrorMessage(text)
      setModalFeedback(text)
    }
  }

  function patchValuationDraft(side: ValuationSide, patch: Partial<ValuationPosition>) {
    setValuationDrafts((current) => ({ ...current, [side]: { ...current[side], ...patch } }))
  }

  async function saveValuationPosition(side: ValuationSide) {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    try {
      setErrorMessage('')
      const payload = { ...valuationDrafts[side], caseId, side }
      const saved = await api<ValuationPosition>('/api/valuation-positions', { method: 'POST', body: JSON.stringify(payload) })
      const positions = await api<ValuationPosition[]>(`/api/cases/${caseId}/valuation-positions`)
      setValuationPositions(positions)
      setValuationDrafts((current) => ({ ...current, [side]: saved }))
      setEditingValuationSide(null)
      setMessage(`${side} valuation position saved.`)
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to save the valuation position.')
    }
  }

  async function persistComparableSale(draft: ComparableSale, successMessage: string, closeAfterSave: boolean) {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    if (!draft.saleDescription?.trim()) {
      setModalFeedback('Enter a sale description before saving.', { saleDescription: 'Sale description is required.' })
      return
    }

    try {
      setErrorMessage('')
      clearModalFeedback()
      const payload = { ...draft, caseId }
      await api<ComparableSale>('/api/comparable-sales', { method: 'POST', body: JSON.stringify(payload) })
      const sales = await api<ComparableSale[]>(`/api/cases/${caseId}/comparable-sales`)
      setComparableSales(sales)
      setComparableSaleDraft(emptyComparableSale(caseId, draft.side))
      if (closeAfterSave) setActiveModal(null)
      setModalDirty(false)
      setMessage(successMessage)
    } catch (error) {
      const text = error instanceof Error ? error.message : 'Unable to save the comparable sale.'
      setErrorMessage(text)
      setModalFeedback(text)
    }
  }

  async function saveComparableSale(event: FormEvent) {
    event.preventDefault()
    await persistComparableSale(comparableSaleDraft, 'Comparable sale saved.', true)
  }

  async function deleteComparableSale(id: number) {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    if (!window.confirm('Delete this comparable sale?')) return
    try {
      setErrorMessage('')
      await api(`/api/comparable-sales/${id}`, { method: 'DELETE' })
      const sales = await api<ComparableSale[]>(`/api/cases/${caseId}/comparable-sales`)
      setComparableSales(sales)
      setMessage('Comparable sale deleted.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to delete the comparable sale.')
    }
  }

  async function persistWitness(draft: Witness, successMessage: string, closeAfterSave: boolean) {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    if (!draft.name.trim()) {
      setModalFeedback('Enter a witness name before saving.', { name: 'Name is required.' })
      return
    }

    try {
      setErrorMessage('')
      clearModalFeedback()
      const payload = { ...draft, caseId }
      await api<Witness>('/api/witnesses', { method: 'POST', body: JSON.stringify(payload) })
      const loaded = await api<Witness[]>(`/api/cases/${caseId}/witnesses`)
      setWitnesses(loaded)
      setWitnessDraft(emptyWitness(caseId))
      if (closeAfterSave) setActiveModal(null)
      setModalDirty(false)
      setMessage(successMessage)
    } catch (error) {
      const text = error instanceof Error ? error.message : 'Unable to save the witness.'
      setErrorMessage(text)
      setModalFeedback(text)
    }
  }

  async function saveWitness(event: FormEvent) {
    event.preventDefault()
    await persistWitness(witnessDraft, 'Witness saved.', true)
  }

  async function deleteWitness(id: number) {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    if (!window.confirm('Delete this witness?')) return
    try {
      setErrorMessage('')
      await api(`/api/witnesses/${id}`, { method: 'DELETE' })
      const loaded = await api<Witness[]>(`/api/cases/${caseId}/witnesses`)
      setWitnesses(loaded)
      setMessage('Witness deleted.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to delete the witness.')
    }
  }

  async function persistExhibit(draft: Exhibit, successMessage: string, closeAfterSave: boolean) {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    if (!draft.label.trim()) {
      setModalFeedback('Enter an exhibit label before saving.', { label: 'Label is required.' })
      return
    }

    try {
      setErrorMessage('')
      clearModalFeedback()
      const payload = { ...draft, caseId }
      await api<Exhibit>('/api/exhibits', { method: 'POST', body: JSON.stringify(payload) })
      const loaded = await api<Exhibit[]>(`/api/cases/${caseId}/exhibits`)
      setExhibits(loaded)
      setExhibitDraft(emptyExhibit(caseId))
      if (closeAfterSave) setActiveModal(null)
      setModalDirty(false)
      setMessage(successMessage)
    } catch (error) {
      const text = error instanceof Error ? error.message : 'Unable to save the exhibit.'
      setErrorMessage(text)
      setModalFeedback(text)
    }
  }

  async function saveExhibit(event: FormEvent) {
    event.preventDefault()
    await persistExhibit(exhibitDraft, 'Exhibit saved.', true)
  }

  async function deleteExhibit(id: number) {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    if (!window.confirm('Delete this exhibit?')) return
    try {
      setErrorMessage('')
      await api(`/api/exhibits/${id}`, { method: 'DELETE' })
      const loaded = await api<Exhibit[]>(`/api/cases/${caseId}/exhibits`)
      setExhibits(loaded)
      setMessage('Exhibit deleted.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to delete the exhibit.')
    }
  }

  async function persistTrialMotion(draft: TrialMotion, successMessage: string, closeAfterSave: boolean) {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    if (!draft.title.trim()) {
      setModalFeedback('Enter a motion title before saving.', { title: 'Title is required.' })
      return
    }

    try {
      setErrorMessage('')
      clearModalFeedback()
      const payload = { ...draft, caseId }
      await api<TrialMotion>('/api/trial-motions', { method: 'POST', body: JSON.stringify(payload) })
      const loaded = await api<TrialMotion[]>(`/api/cases/${caseId}/trial-motions`)
      setTrialMotions(loaded)
      setTrialMotionDraft(emptyTrialMotion(caseId))
      if (closeAfterSave) setActiveModal(null)
      setModalDirty(false)
      setMessage(successMessage)
    } catch (error) {
      const text = error instanceof Error ? error.message : 'Unable to save the trial motion.'
      setErrorMessage(text)
      setModalFeedback(text)
    }
  }

  async function saveTrialMotion(event: FormEvent) {
    event.preventDefault()
    await persistTrialMotion(trialMotionDraft, 'Trial motion saved.', true)
  }

  async function deleteTrialMotion(id: number) {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    if (!window.confirm('Delete this trial motion?')) return
    try {
      setErrorMessage('')
      await api(`/api/trial-motions/${id}`, { method: 'DELETE' })
      const loaded = await api<TrialMotion[]>(`/api/cases/${caseId}/trial-motions`)
      setTrialMotions(loaded)
      setMessage('Trial motion deleted.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to delete the trial motion.')
    }
  }

  async function saveCase(event: FormEvent) {
    event.preventDefault()
    const previous = caseDraft.id ? selectedCase : null
    const payload = serializeCaseDraft(caseDraft)
    const validation = validateCaseDraft(payload)
    if (Object.keys(validation.fieldErrors).length > 0) {
      setModalFeedback(validation.summary, validation.fieldErrors)
      return
    }

    try {
      setErrorMessage('')
      clearModalFeedback()
      const saved = await api<CaseRecord>('/api/cases', { method: 'POST', body: JSON.stringify(payload) })
      if (previous && saved.caseStatus === 'Pipeline') {
        const changes: string[] = []
        if ((previous.currentHolder || '') !== (saved.currentHolder || '')) changes.push(`Holder changed to ${saved.currentHolder || 'unassigned'}.`)
        if ((previous.nextReviewDate || '') !== (saved.nextReviewDate || '')) changes.push(`Next review date changed to ${saved.nextReviewDate || 'not set'}.`)
        if ((previous.shortPostureSummary || '') !== (saved.shortPostureSummary || '')) changes.push('Pipeline note updated.')
        for (const notes of changes) await api(`/api/cases/${saved.id}/activity`, { method: 'POST', body: JSON.stringify({ activityType: 'PipelineUpdated', notes }) })
      }
      setSelectedCaseId(saved.id)
      setCasesView('workspace')
      setCaseTab('overview')
      setActiveModal(null)
      setModalDirty(false)
      await refreshAll(saved.id)
      setMessage(`Saved ${saved.caseNumber || saved.caseName}.`)
    } catch (error) {
      const text = error instanceof Error ? error.message : 'Unable to save case.'
      setErrorMessage(text)
      setModalFeedback(text)
    }
  }

  async function saveDeadline(event: FormEvent) {
    event.preventDefault()
    await persistDeadline(deadlineDraft, 'Deadline saved.', true)
  }

  async function saveChecklist(event: FormEvent) {
    event.preventDefault()
    await persistChecklist(checklistDraft, 'Checklist task saved.', true)
  }

  async function saveDiscovery(event: FormEvent) {
    event.preventDefault()
    await persistDiscovery(discoveryDraft, 'Discovery item saved.', true)
  }

  async function savePublication(event: FormEvent) {
    event.preventDefault()
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    try {
      setErrorMessage('')
      const saved = await api<PublicationRecord>(`/api/cases/${caseId}/publication`, { method: 'PUT', body: JSON.stringify({ ...publicationDraft, caseId }) })
      setPublicationDraft(saved)
      await refreshAll(caseId)
      setMessage('Publication details saved.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to save publication details.')
    }
  }

  async function recordDiscoveryResponse(item: DiscoveryItem) {
    const responseDate = window.prompt('Response date (YYYY-MM-DD)', new Date().toISOString().slice(0, 10))?.trim()
    if (!responseDate || !/^\d{4}-\d{2}-\d{2}$/.test(responseDate)) return
    try {
      await api<DiscoveryItem>('/api/discovery', { method: 'POST', body: JSON.stringify(serializeDiscoveryDraft({ ...item, responseDate, status: 'Complete' }, item.caseId)) })
      await refreshAll(item.caseId)
      setMessage('Discovery response recorded from the Work Queue.')
    } catch (error) { setErrorMessage(error instanceof Error ? error.message : 'Unable to record the discovery response.') }
  }

  async function saveDeadlineTemplate() {
    if (!deadlineTemplateDraft) return
    try {
      await api('/api/deadline-templates', { method:'POST', body:JSON.stringify(deadlineTemplateDraft) })
      setDeadlineTemplates(await api<DeadlineTemplate[]>('/api/deadline-templates')); setDeadlineTemplateDraft(null)
      setMessage('Deadline template saved.')
    } catch(error) { setErrorMessage(error instanceof Error ? error.message : 'Unable to save deadline template.') }
  }

  async function clearDeferment() {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    try {
      const version = selectedCase.rowVersion ? `&rowVersion=${encodeURIComponent(selectedCase.rowVersion)}` : ''
      const result = await api<{ rowVersion?: string | null }>(`/api/cases/${caseId}/defer?reason=${encodeURIComponent('Cleared from Status tab')}${version}`, { method: 'DELETE' })
      applyCaseRowVersion(caseId, result.rowVersion)
      await refreshAll(caseId)
      setMessage('Deferment cleared.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to clear deferment.')
    }
  }

  async function saveDefermentDate() {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId || !defermentDateDraft) return
    try {
      const result = await api<{ rowVersion?: string | null }>(`/api/cases/${caseId}/defer`, { method: 'POST', body: JSON.stringify({ rowVersion: selectedCase.rowVersion, reason: selectedCase.deferredReason ?? '', futureReviewDate: defermentDateDraft }) })
      applyCaseRowVersion(caseId, result.rowVersion)
      setDefermentDateEditOpen(false)
      await refreshAll(caseId)
      setMessage('Deferment date updated.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to update deferment date.')
    }
  }

  async function addIssueTag() {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId || !selectedTagId) return
    try {
      setErrorMessage('')
      setIssueTagMessage('')
      const result = await api<{ message?: string }>(`/api/cases/${caseId}/issue-tags/${selectedTagId}`, { method: 'POST' })
      await refreshAll(caseId)
      setSelectedTagId(0)
      setMessage(result.message || 'Issue tag added.')
    } catch (error) {
      const text = error instanceof Error ? error.message : 'Unable to add issue tag.'
      setIssueTagMessage(text)
      setErrorMessage(text)
    }
  }

  async function removeIssueTag(id: number) {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    try {
      setErrorMessage('')
      setIssueTagMessage('')
      const assigned = workspace?.caseIssueTags.find((tag) => tag.id === id)
      const version = assigned?.rowVersion ? `?rowVersion=${encodeURIComponent(assigned.rowVersion)}` : ''
      await api(`/api/case-issue-tags/${id}${version}`, { method: 'DELETE' })
      await refreshAll(caseId)
      setMessage('Issue tag removed.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to remove issue tag.')
    }
  }

  async function generateDocument(kind: 'summary' | 'memo') {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    try {
      setErrorMessage('')
      const saved = await api<DocumentExport>(`/api/cases/${caseId}/generate/${kind}`, { method: 'POST' })
      await refreshAll(caseId)
      setCaseTab('documents')
      setMessage(`${saved.documentTitle} generated.`)
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to generate document.')
    }
  }

  // Build-plan step 4: the new unified document-platform generation flow. Loads the checklist
  // pre-checked from the case's actual issue tags, lets the attorney toggle it, then generates
  // and records history through document_generations - separate from the legacy docGen flow
  // above, which this is meant to eventually replace. Step 6 generalized this from a single
  // hardcoded Interrogatories button to a picker over every registered template, each with its
  // own runtime-input fields (Judgment, Settlement Justification, and Requests for Admission all
  // need manual fields the seed template never had).
  async function loadDocumentPlatformCaseTemplates() {
    try {
      setErrorMessage('')
      const templates = await api<DocumentTemplateAdminSummary[]>('/api/document-platform/templates')
      setPlatformTemplates(templates)
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to load document templates.')
    }
  }

  async function loadPlatformChecklist(templateKey: string) {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId || !templateKey) return
    try {
      setErrorMessage('')
      setPlatformGenerationResult(null)
      const checklist = await api<DocumentGenerationChecklist>(`/api/cases/${caseId}/document-platform/templates/${templateKey}/checklist`)
      setPlatformChecklist(checklist)
      setPlatformCaseTemplateKey(templateKey)
      setPlatformSelectedSections(checklist.sections.filter((s) => s.isDefaultChecked).map((s) => s.sectionKey))
      setPlatformRuntimeInputValues({})
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to load the document checklist.')
    }
  }

  function togglePlatformSection(sectionKey: string) {
    setPlatformSelectedSections((current) =>
      current.includes(sectionKey) ? current.filter((key) => key !== sectionKey) : [...current, sectionKey])
  }

  function setPlatformRuntimeInputValue(fieldKey: string, value: string) {
    setPlatformRuntimeInputValues((current) => ({ ...current, [fieldKey]: value }))
  }

  async function generatePlatformDocument() {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId || !platformChecklist) return
    try {
      setPlatformBusy(true)
      setErrorMessage('')
      const result = await api<DocumentGenerationResult>(
        `/api/cases/${caseId}/document-platform/templates/${platformChecklist.templateKey}/generate`,
        { method: 'POST', body: JSON.stringify({ selectedSectionKeys: platformSelectedSections, runtimeInputValues: platformRuntimeInputValues, outputFileName: null }) })
      setPlatformGenerationResult(result)
      void loadPlatformGenerationHistory(caseId)
      setMessage('Document generated.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to generate document.')
    } finally {
      setPlatformBusy(false)
    }
  }

  // Build-plan step 5: Document Templates admin (upload, section/overlap/runtime-input
  // configuration, version activation) and Issue Tags admin (create/rename/retire, usage lookup).
  async function loadPlatformTemplates() {
    try {
      setErrorMessage('')
      const templates = await api<DocumentTemplateAdminSummary[]>('/api/document-platform/templates')
      setPlatformTemplates(templates)
      if (selectedPlatformTemplateKey) {
        const refreshed = templates.find((t) => t.template.templateKey === selectedPlatformTemplateKey)
        if (refreshed) selectPlatformTemplate(refreshed)
      }
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to load document templates.')
    }
  }

  function selectPlatformTemplate(summary: DocumentTemplateAdminSummary) {
    setSelectedPlatformTemplateKey(summary.template.templateKey)
    setPlatformConfigDraft({ sections: summary.sections, overlaps: summary.overlaps, runtimeInputs: summary.runtimeInputs })
  }

  async function uploadPlatformTemplate() {
    if (!platformUploadFile) {
      setErrorMessage('Choose a .docx file to upload.')
      return
    }
    try {
      setErrorMessage('')
      const form = new FormData()
      form.set('templateKey', platformUploadDraft.templateKey)
      form.set('title', platformUploadDraft.title)
      form.set('description', platformUploadDraft.description)
      form.set('category', platformUploadDraft.category)
      form.set('file', platformUploadFile)
      const accessToken = await getApiAccessToken()
      const headers = new Headers()
      if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`)
      const response = await fetch('/api/document-platform/templates/upload', { method: 'POST', body: form, headers })
      if (!response.ok) {
        const parsed = await response.json().catch(() => null) as ApiError | null
        throw new Error(parsed?.error ?? 'Unable to upload template.')
      }
      setMessage('Template uploaded.')
      setPlatformUploadDraft({ templateKey: '', title: '', description: '', category: '' })
      setPlatformUploadFile(null)
      setPlatformUploadKeyLocked(false)
      await loadPlatformTemplates()
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to upload template.')
    }
  }

  function startUploadNewVersion(summary: DocumentTemplateAdminSummary) {
    setPlatformUploadDraft({
      templateKey: summary.template.templateKey,
      title: summary.template.title,
      description: summary.template.description || '',
      category: summary.template.category,
    })
    setPlatformUploadFile(null)
    setPlatformUploadKeyLocked(true)
  }

  function startNewPlatformTemplateUpload() {
    setPlatformUploadDraft({ templateKey: '', title: '', description: '', category: '' })
    setPlatformUploadFile(null)
    setPlatformUploadKeyLocked(false)
  }

  async function savePlatformConfiguration() {
    if (!selectedPlatformTemplateKey) return
    try {
      setErrorMessage('')
      await api(`/api/document-platform/templates/${selectedPlatformTemplateKey}/configuration`, {
        method: 'PUT',
        body: JSON.stringify(platformConfigDraft),
      })
      setMessage('Configuration saved.')
      await loadPlatformTemplates()
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to save configuration.')
    }
  }

  async function activatePlatformVersion(templateKey: string, version: number) {
    try {
      setErrorMessage('')
      await api(`/api/document-platform/templates/${templateKey}/versions/${version}/activate`, { method: 'POST' })
      setMessage(`Version ${version} activated.`)
      await loadPlatformTemplates()
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to activate version.')
    }
  }

  async function deletePlatformTemplate(templateKey: string) {
    try {
      setErrorMessage('')
      await api(`/api/document-platform/templates/${templateKey}`, { method: 'DELETE' })
      setMessage('Template deleted.')
      if (selectedPlatformTemplateKey === templateKey) setSelectedPlatformTemplateKey(null)
      await loadPlatformTemplates()
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to delete template.')
    }
  }

  function addSectionDraft() {
    if (!newSectionDraft.sectionKey.trim() || !newSectionDraft.label.trim()) return
    setPlatformConfigDraft((current) => ({
      ...current,
      sections: [...current.sections, {
        sectionKey: newSectionDraft.sectionKey.trim(),
        label: newSectionDraft.label.trim(),
        description: newSectionDraft.description || null,
        issueTagName: newSectionDraft.issueTagName || null,
        sortOrder: current.sections.length,
      }],
    }))
    setNewSectionDraft({ sectionKey: '', label: '', description: '', issueTagName: '' })
  }

  function removeSectionDraft(sectionKey: string) {
    setPlatformConfigDraft((current) => ({
      ...current,
      sections: current.sections.filter((s) => s.sectionKey !== sectionKey),
      overlaps: current.overlaps.filter((o) => o.sectionAKey !== sectionKey && o.sectionBKey !== sectionKey),
    }))
  }

  function addOverlapDraft() {
    if (!newOverlapDraft.sectionAKey || !newOverlapDraft.sectionBKey || newOverlapDraft.sectionAKey === newOverlapDraft.sectionBKey) return
    setPlatformConfigDraft((current) => ({ ...current, overlaps: [...current.overlaps, { ...newOverlapDraft, note: newOverlapDraft.note || null }] }))
    setNewOverlapDraft({ sectionAKey: '', sectionBKey: '', note: '' })
  }

  function removeOverlapDraft(index: number) {
    setPlatformConfigDraft((current) => ({ ...current, overlaps: current.overlaps.filter((_, i) => i !== index) }))
  }

  function addRuntimeInputDraft() {
    if (!newRuntimeInputDraft.fieldKey.trim() || !newRuntimeInputDraft.label.trim()) return
    setPlatformConfigDraft((current) => ({
      ...current,
      runtimeInputs: [...current.runtimeInputs, { ...newRuntimeInputDraft, fieldKey: newRuntimeInputDraft.fieldKey.trim(), sortOrder: current.runtimeInputs.length }],
    }))
    setNewRuntimeInputDraft({ fieldKey: '', label: '', fieldType: 'text', isRequired: true })
  }

  function removeRuntimeInputDraft(fieldKey: string) {
    setPlatformConfigDraft((current) => ({ ...current, runtimeInputs: current.runtimeInputs.filter((i) => i.fieldKey !== fieldKey) }))
  }

  async function loadIssueTagUsage() {
    try {
      setErrorMessage('')
      setIssueTagUsage(await api<IssueTagUsage[]>('/api/issue-tags/usage'))
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to load issue tag usage.')
    }
  }

  async function createIssueTagFromSettings() {
    if (!newIssueTagDraft.name.trim()) {
      setErrorMessage('A tag name is required.')
      return
    }
    try {
      setErrorMessage('')
      await api<IssueTag>('/api/issue-tags', {
        method: 'POST',
        body: JSON.stringify({ name: newIssueTagDraft.name.trim(), description: newIssueTagDraft.description || null, category: newIssueTagDraft.category || null }),
      })
      setMessage('Issue tag created.')
      setNewIssueTagDraft({ name: '', description: '', category: '' })
      await refreshAll()
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to create issue tag.')
    }
  }

  async function saveIssueTagRename() {
    if (!issueTagEditDraft) return
    try {
      setErrorMessage('')
      await api<IssueTag>(`/api/issue-tags/${issueTagEditDraft.id}`, {
        method: 'PUT',
        body: JSON.stringify({ name: issueTagEditDraft.name, description: issueTagEditDraft.description || null, category: issueTagEditDraft.category || null }),
      })
      setMessage('Issue tag updated.')
      setIssueTagEditDraft(null)
      await refreshAll()
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to update issue tag.')
    }
  }

  async function retireIssueTagFromSettings(id: number) {
    try {
      setErrorMessage('')
      await api(`/api/issue-tags/${id}`, { method: 'DELETE' })
      setMessage('Issue tag retired.')
      await refreshAll()
      await loadIssueTagUsage()
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to retire issue tag.')
    }
  }

  async function saveOrgDefaults() {
    try {
      setErrorMessage('')
      const saved = await api<OrgDefaults>('/api/org-defaults', { method: 'POST', body: JSON.stringify(orgDefaults) })
      setOrgDefaults(saved)
      setMessage('Document defaults saved.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to save document defaults.')
    }
  }

  async function saveCaseNote() {
    const caseId = selectedCaseId ?? selectedCase.id
    if (!caseId) return
    if (!noteDraft.body.trim()) {
      setErrorMessage('Enter note text before saving.')
      return
    }
    try {
      setErrorMessage('')
      await api<CaseNote>('/api/case-notes', {
        method: 'POST',
        body: JSON.stringify({
          ...noteDraft,
          caseId,
          title: noteDraft.title.trim() || 'Untitled Note',
          body: noteDraft.body.trim(),
        }),
      })
      await refreshAll(caseId)
      setNoteDraft({ id: 0, caseId, title: '', body: '', createdAt: '', updatedAt: '' })
      setMessage('Case note saved.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to save the case note.')
    }
  }

  async function deleteCaseNote(note: CaseNote) {
    const caseId = selectedCaseId ?? selectedCase.id
    if (!caseId) return
    if (!window.confirm(`Delete note "${note.title}"?`)) return
    try {
      setErrorMessage('')
      await api(`/api/case-notes/${note.id}`, { method: 'DELETE' })
      await refreshAll(caseId)
      if (noteDraft.id === note.id) {
        setNoteDraft({ id: 0, caseId, title: '', body: '', createdAt: '', updatedAt: '' })
      }
      setMessage('Case note deleted.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to delete the case note.')
    }
  }

  async function saveHearing(event: FormEvent) {
    event.preventDefault()
    const caseId = selectedCaseId ?? selectedCase.id
    if (!caseId) return
    if (!hearingDraft.title.trim()) {
      setModalFeedback('Enter an event title before saving.', { title: 'Title is required.' })
      return
    }
    try {
      setErrorMessage('')
      clearModalFeedback()
      await api<Hearing>('/api/hearings', {
        method: 'POST',
        body: JSON.stringify({
          ...hearingDraft,
          caseId,
          title: hearingDraft.title.trim(),
          eventType: hearingDraft.eventType?.trim() || 'Hearing',
          hearingDate: normalizeDateValue(hearingDraft.hearingDate),
          location: normalizeTextValue(hearingDraft.location),
          description: normalizeTextValue(hearingDraft.description),
        }),
      })
      await refreshAll(caseId)
      setHearingDraft(emptyHearing(caseId))
      setActiveModal(null)
      setModalDirty(false)
      setMessage('Event saved.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to save the event.')
    }
  }

  async function deleteHearing(hearing: Hearing) {
    const caseId = selectedCaseId ?? selectedCase.id
    if (!caseId) return
    if (!window.confirm(`Delete event "${hearing.title}"?`)) return
    try {
      setErrorMessage('')
      await api(`/api/hearings/${hearing.id}`, { method: 'DELETE' })
      await refreshAll(caseId)
      if (hearingDraft.id === hearing.id) {
        setHearingDraft(emptyHearing(caseId))
      }
      setMessage('Event deleted.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to delete the event.')
    }
  }

  function emptyChecklistTemplate(): ChecklistTemplate {
    return { id: 0, name: '', triggerType: 'Stage', stage: 'Pipeline', issueTagName: '', track: 'Any', active: true, items: [] }
  }

  function startNewChecklistTemplate() {
    setTemplateDraft(emptyChecklistTemplate())
  }

  function startEditChecklistTemplate(template: ChecklistTemplate) {
    setTemplateDraft({ ...template })
  }

  async function saveChecklistTemplate() {
    if (!templateDraft) return
    if (!templateDraft.name.trim()) {
      setErrorMessage('Template name is required.')
      return
    }
    try {
      setErrorMessage('')
      await api<ChecklistTemplate>('/api/checklist-templates', { method: 'POST', body: JSON.stringify(templateDraft) })
      const refreshed = await api<ChecklistTemplate[]>('/api/checklist-templates')
      setChecklistTemplates(refreshed)
      setTemplateDraft(null)
      setMessage('Checklist template saved.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to save the checklist template.')
    }
  }

  async function deleteChecklistTemplate(template: ChecklistTemplate) {
    if (!window.confirm(`Delete template "${template.name}" and its ${template.items.length} item(s)? This cannot be undone.`)) return
    try {
      setErrorMessage('')
      const version = template.rowVersion ? `?rowVersion=${encodeURIComponent(template.rowVersion)}` : ''
      await api(`/api/checklist-templates/${template.id}${version}`, { method: 'DELETE' })
      const refreshed = await api<ChecklistTemplate[]>('/api/checklist-templates')
      setChecklistTemplates(refreshed)
      setMessage('Checklist template deleted.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to delete the checklist template.')
    }
  }

  function emptyTemplateItem(templateId: number, nextSortOrder: number): ChecklistTemplateItem {
    return { id: 0, templateId, task: '', phase: '', sortOrder: nextSortOrder, dueOffsetDays: null }
  }

  function startNewTemplateItem(template: ChecklistTemplate) {
    setTemplateItemDraft(emptyTemplateItem(template.id, template.items.length))
  }

  function startEditTemplateItem(item: ChecklistTemplateItem) {
    setTemplateItemDraft({ ...item })
  }

  async function saveTemplateItem() {
    if (!templateItemDraft) return
    if (!templateItemDraft.task.trim()) {
      setErrorMessage('Task text is required.')
      return
    }
    try {
      setErrorMessage('')
      await api<ChecklistTemplateItem>('/api/checklist-template-items', { method: 'POST', body: JSON.stringify(templateItemDraft) })
      const refreshed = await api<ChecklistTemplate[]>('/api/checklist-templates')
      setChecklistTemplates(refreshed)
      setTemplateItemDraft(null)
      setMessage('Checklist template item saved.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to save the checklist template item.')
    }
  }

  async function deleteTemplateItem(item: ChecklistTemplateItem) {
    if (!window.confirm(`Delete task "${item.task}"?`)) return
    try {
      setErrorMessage('')
      const version = item.rowVersion ? `?rowVersion=${encodeURIComponent(item.rowVersion)}` : ''
      await api(`/api/checklist-template-items/${item.id}${version}`, { method: 'DELETE' })
      const refreshed = await api<ChecklistTemplate[]>('/api/checklist-templates')
      setChecklistTemplates(refreshed)
      setMessage('Checklist template item deleted.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to delete the checklist template item.')
    }
  }

  async function createBackupNow() {
    try {
      setErrorMessage('')
      await api<BackupInfo>('/api/backups', { method: 'POST' })
      const refreshed = await api<BackupInfo[]>('/api/backups')
      setBackups(refreshed)
      setMessage('Backup created.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to create a backup.')
    }
  }

  async function restoreFromBackup(backup: BackupInfo) {
    if (!window.confirm(`Restore the database to the backup from ${displayDateTime(backup.createdAt)}? Your current data will be saved as a new backup first, then replaced. Any case you have open will close.`)) return
    const confirmation = window.prompt('Type RESTORE to confirm.')
    if (confirmation !== 'RESTORE') {
      setMessage('Restore canceled.')
      return
    }
    try {
      setErrorMessage('')
      await api('/api/backups/restore', { method: 'POST', body: JSON.stringify({ fileName: backup.fileName }) })
      setSelectedCaseId(null)
      setWorkspace(null)
      setPage('dashboard')
      await refreshAll(null)
      setMessage('Database restored from backup.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to restore the backup.')
    }
  }

  async function exportCaseNotes() {
    const caseId = selectedCaseId ?? selectedCase.id
    if (!caseId) return
    try {
      setErrorMessage('')
      const result = await api<{ title: string; outputPath: string }>(`/api/cases/${caseId}/export-notes`, { method: 'POST' })
      await navigator.clipboard.writeText(result.outputPath)
      setMessage(`${result.title} exported. Output path copied.`)
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to export case notes.')
    }
  }

  async function deleteSelectedCase() {
    const caseId = selectedCaseId ?? selectedCase.id
    if (!caseId) return
    if (!window.confirm(`Delete case "${selectedCase.caseName}"? This permanently removes the case and its related work items.`)) return
    const confirmation = window.prompt(`Type DELETE to permanently remove ${selectedCase.caseNumber || selectedCase.caseName}.`)
    if (confirmation !== 'DELETE') {
      setMessage('Case deletion canceled.')
      return
    }
    try {
      setErrorMessage('')
      await api(`/api/cases/${caseId}`, { method: 'DELETE' })
      await loadInitial()
      goToCaseList()
      setMessage(`Case ${selectedCase.caseNumber} deleted.`)
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to delete the case.')
    }
  }

  async function deleteSampleData() {
    if (!diagnostics?.sampleDataExists) { setMessage('No recognized sample data is present.'); return }
    if (!window.confirm('Delete the recognized fictional sample case and its related records? Your real cases will not be touched.')) return
    try {
      const result = await api<{ deleted: number }>('/api/data-management/sample-data/delete', { method: 'POST' })
      await refreshAll(null)
      setMessage(result.deleted ? 'Fictional sample data deleted.' : 'No recognized sample data was found.')
    } catch (error) { setErrorMessage(error instanceof Error ? error.message : 'Unable to delete sample data.') }
  }

  async function resetEntireDatabase() {
    const confirmation = window.prompt('This permanently replaces the database after creating a verified backup. Type RESET CASE PLANNER to continue.')
    if (confirmation !== 'RESET CASE PLANNER') { setMessage('Database reset canceled.'); return }
    const scope = window.confirm('Also delete generated exports? Choose OK for database + generated content, or Cancel for database only.') ? 'database-and-generated-content' : 'database'
    try {
      await api('/api/data-management/reset', { method: 'POST', body: JSON.stringify({ scope, confirmation }) })
      setSelectedCaseId(null); setWorkspace(null); setPage('dashboard')
      await refreshAll(null)
      setMessage('Database reset completed and one fictional sample case was reseeded.')
    } catch (error) { setErrorMessage(error instanceof Error ? error.message : 'Unable to reset the database.') }
  }

  function sampleMergeTagValue(key: string) {
    const values: Record<string, string> = {
      County: selectedCase.county || '', CaseNumber: selectedCase.caseNumber || '', JobNumber: selectedCase.jobNumber || '',
      Tract: selectedCase.tract || '', ProjectName: selectedCase.projectName || '', DefendantNames: selectedCase.landowner || selectedCase.owner || '',
      AttorneyName: orgDefaults.attorneyName || '', BarNumber: orgDefaults.barNumber || '', AttorneyPhone: orgDefaults.phone || '', AttorneyEmail: orgDefaults.email || '',
      OrgAddressLine1: orgDefaults.addressLine1 || '', OrgAddressLine2: orgDefaults.addressLine2 || ''
    }
    return values[key] || 'Example value / entered at generation'
  }

  async function recomputeRiskAnalysis(rows: RiskAnalysisRowInput[], narrative: string) {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    try {
      setErrorMessage('')
      const result = await api<RiskAnalysisResult>(`/api/cases/${caseId}/risk-analysis/preview`, {
        method: 'POST',
        body: JSON.stringify({ caseId, narrative, analysisDate: riskAnalysisDate, interestRate: riskAnalysisInterestRate, contingencyFeePercent: riskAnalysisContingencyPercent, rows }),
      })
      setRiskAnalysisPreview(result)
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to recalculate the Risk Analysis.')
    }
  }

  async function patchRiskAnalysisRow(rowKey: string, patch: Partial<RiskAnalysisRowInput>) {
    const updatedRows = riskAnalysisRows.map((row) => (row.rowKey === rowKey ? { ...row, ...patch } : row))
    setRiskAnalysisRows(updatedRows)
    await recomputeRiskAnalysis(updatedRows, riskAnalysisNarrative)
  }

  async function saveRiskAnalysis() {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    try {
      setErrorMessage('')
      setRiskAnalysisSaving(true)
      const saved = await api<RiskAnalysisResult>(`/api/cases/${caseId}/risk-analysis`, {
        method: 'POST',
        body: JSON.stringify({ caseId, narrative: riskAnalysisNarrative, analysisDate: riskAnalysisDate, interestRate: riskAnalysisInterestRate, contingencyFeePercent: riskAnalysisContingencyPercent, rows: riskAnalysisRows }),
      })
      setRiskAnalysisPreview(saved)
      setRiskAnalysisEditorOpen(false)
      setRiskAnalysisHistory(await api<RiskAnalysisHistoryRecord[]>(`/api/cases/${caseId}/risk-analysis/history`))
      setMessage('Risk Analysis saved.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to save the Risk Analysis.')
    } finally {
      setRiskAnalysisSaving(false)
    }
  }

  async function openRiskAnalysisHistory(id: number) {
    if (!selectedCaseId) return
    try {
      const snapshot = await api<RiskAnalysisResult>(`/api/cases/${selectedCaseId}/risk-analysis/history/${id}`)
      setRiskAnalysisPreview(snapshot)
      setRiskAnalysisEditorOpen(true)
      setRiskAnalysisDate(snapshot.analysisDate || new Date().toISOString().slice(0, 10))
      setRiskAnalysisInterestRate(snapshot.interestRate || 0.06)
      setRiskAnalysisContingencyPercent(snapshot.contingencyFeePercent || 0.30)
      setRiskAnalysisNarrative(snapshot.narrative ?? '')
      setRiskAnalysisRows(snapshot.rows.filter((row) => !row.isSplit).map((row) => ({ rowKey: row.rowKey, label: row.label, offerMaker: row.offerMaker, includeSplit: row.includeSplit, justCompensation: row.justCompensation, landownerFeesCosts: row.landownerFeesCosts, ashcCosts: row.ashcCosts, hourlyFeesRisk: row.hourlyFeesRisk })))
      setMessage('Historical risk analysis opened for review.')
    } catch (error) { setErrorMessage(error instanceof Error ? error.message : 'Unable to open historical risk analysis.') }
  }

  async function deleteRiskAnalysisHistory(entry: RiskAnalysisHistoryRecord) {
    if (!selectedCaseId) return
    const scenario = entry.keyScenarioLabel ? `${entry.keyScenarioLabel}${entry.keyScenarioValue != null ? ` — ${displayCurrency(entry.keyScenarioValue)}` : ''}` : 'no populated key scenario'
    if (!window.confirm(`Delete the risk analysis from ${displayDate(entry.analysisDate)} (${scenario})? This action will remove the saved analysis and cannot be undone.`)) return
    try {
      await api(`/api/cases/${selectedCaseId}/risk-analysis/history/${entry.id}`, { method: 'DELETE' })
      await api(`/api/cases/${selectedCaseId}/activity`, { method: 'POST', body: JSON.stringify({ activityType: 'RiskAnalysisDeleted', notes: `Saved risk analysis deleted (${displayDate(entry.analysisDate)}).` }) })
      setRiskAnalysisHistory((current) => current.filter((item) => item.id !== entry.id))
      setMessage('Saved risk analysis deleted.')
    } catch (error) { setErrorMessage(error instanceof Error ? error.message : 'Unable to delete saved risk analysis.') }
  }

  async function compareRiskAnalysisHistory(entry: RiskAnalysisHistoryRecord) {
    if (!selectedCaseId) return
    try {
      const snapshot = await api<RiskAnalysisResult>(`/api/cases/${selectedCaseId}/risk-analysis/history/${entry.id}`)
      setRiskAnalysisComparison({ left: entry, right: snapshot })
    } catch (error) { setErrorMessage(error instanceof Error ? error.message : 'Unable to compare saved risk analysis.') }
  }

  async function resetRiskAnalysis() {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    if (!window.confirm("Reset this case's Risk Analysis ledger back to zero? This clears all saved values.")) return
    try {
      setErrorMessage('')
      await api(`/api/cases/${caseId}/risk-analysis`, { method: 'DELETE' })
      const result = await api<RiskAnalysisResult>(`/api/cases/${caseId}/risk-analysis`)
      setRiskAnalysisPreview(result)
      setRiskAnalysisNarrative(result.narrative ?? '')
      setRiskAnalysisRows(defaultRiskAnalysisRows())
      setRiskAnalysisEditorOpen(false)
      setMessage('Risk Analysis reset.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to reset the Risk Analysis.')
    }
  }

  function startNewOfferLogEntry() {
    const caseId = selectedCaseId ?? caseDraft.id
    setOfferLogDraft(emptyOfferLogEntry(caseId))
    setOfferLogFormOpen(true)
  }

  function startEditOfferLogEntry(entry: RiskAnalysisOfferLogEntry) {
    setOfferLogDraft(entry)
    setOfferLogFormOpen(true)
  }

  async function saveOfferLogEntry() {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    try {
      setErrorMessage('')
      await api(`/api/risk-analysis-offers`, { method: 'POST', body: JSON.stringify({ ...offerLogDraft, caseId }) })
      const entries = await api<RiskAnalysisOfferLogEntry[]>(`/api/cases/${caseId}/risk-analysis-offers`)
      setOfferLog(entries)
      setOfferLogDraft(emptyOfferLogEntry(caseId))
      setOfferLogFormOpen(false)
      setMessage('Old offer saved.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to save the old offer.')
    }
  }

  async function deleteOfferLogEntry(entry: RiskAnalysisOfferLogEntry) {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId || !window.confirm('Remove this old offer entry?')) return
    try {
      setErrorMessage('')
      await api(`/api/risk-analysis-offers/${entry.id}`, { method: 'DELETE' })
      setOfferLog(offerLog.filter((e) => e.id !== entry.id))
      if (offerLogDraft.id === entry.id) setOfferLogDraft(emptyOfferLogEntry(caseId))
      setMessage('Old offer removed.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to remove the old offer.')
    }
  }

  function startNewServiceLogEntry() {
    const caseId = selectedCaseId ?? caseDraft.id
    setServiceLogDraft(emptyServiceLogEntry(caseId))
    setServiceLogFormOpen(true)
  }

  function startEditServiceLogEntry(entry: ServiceLogEntry) {
    setServiceLogDraft(entry)
    setServiceLogFormOpen(true)
  }

  async function saveServiceLogEntry() {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    if (!serviceLogDraft.partyName.trim()) {
      setErrorMessage('Enter a party name before saving.')
      return
    }
    try {
      setErrorMessage('')
      await api('/api/service-log', { method: 'POST', body: JSON.stringify({ ...serviceLogDraft, caseId }) })
      const entries = await api<ServiceLogEntry[]>(`/api/cases/${caseId}/service-log`)
      setServiceLogEntries(entries)
      setServiceLogDraft(emptyServiceLogEntry(caseId))
      setServiceLogFormOpen(false)
      setMessage('Service log entry saved.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to save the service log entry.')
    }
  }

  async function updateServiceLogStatus(entry: ServiceLogEntry, status: string) {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    try {
      setErrorMessage('')
      await api('/api/service-log', { method: 'POST', body: JSON.stringify({ ...entry, status, caseId }) })
      const entries = await api<ServiceLogEntry[]>(`/api/cases/${caseId}/service-log`)
      setServiceLogEntries(entries)
      setMessage('Service log status updated.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to update the service log entry.')
    }
  }

  async function deleteServiceLogEntry(entry: ServiceLogEntry) {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId || !window.confirm(`Remove the service entry for "${entry.partyName}"?`)) return
    try {
      setErrorMessage('')
      await api(`/api/service-log/${entry.id}`, { method: 'DELETE' })
      setServiceLogEntries(serviceLogEntries.filter((e) => e.id !== entry.id))
      if (serviceLogDraft.id === entry.id) setServiceLogDraft(emptyServiceLogEntry(caseId))
      setMessage('Service log entry removed.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to remove the service log entry.')
    }
  }

  function startNewPublicationEntry() {
    const caseId = selectedCaseId ?? caseDraft.id
    setPublicationEntryDraft(emptyPublicationEntry(caseId))
    setPublicationEntryFormOpen(true)
  }

  function startEditPublicationEntry(entry: PublicationEntry) {
    setPublicationEntryDraft(entry)
    setPublicationEntryFormOpen(true)
  }

  async function savePublicationEntry() {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId) return
    try {
      setErrorMessage('')
      await api('/api/publication-service', { method: 'POST', body: JSON.stringify({ ...publicationEntryDraft, caseId }) })
      const entries = await api<PublicationEntry[]>(`/api/cases/${caseId}/publication-service`)
      setPublicationEntries(entries)
      setPublicationEntryDraft(emptyPublicationEntry(caseId))
      setPublicationEntryFormOpen(false)
      setMessage('Publication entry saved.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to save the publication entry.')
    }
  }

  async function deletePublicationEntry(entry: PublicationEntry) {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId || !window.confirm('Remove this publication entry?')) return
    try {
      setErrorMessage('')
      await api(`/api/publication-service/${entry.id}`, { method: 'DELETE' })
      setPublicationEntries(publicationEntries.filter((e) => e.id !== entry.id))
      if (publicationEntryDraft.id === entry.id) setPublicationEntryDraft(emptyPublicationEntry(caseId))
      setMessage('Publication entry removed.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to remove the publication entry.')
    }
  }

  function startNarrativeGeneration() {
    setNarrativeInputDraft(emptyRiskNarrativeInputs())
  }

  function patchNarrativeInputDraft(patch: Partial<RiskNarrativeManualInputs>) {
    setNarrativeInputDraft((prev) => (prev ? { ...prev, ...patch } : prev))
  }

  async function generateRiskNarrative() {
    const caseId = selectedCaseId ?? caseDraft.id
    if (!caseId || !narrativeInputDraft) return
    try {
      setErrorMessage('')
      setNarrativeGenerating(true)
      const result = await api<{ narrative: string }>(`/api/cases/${caseId}/risk-analysis/narrative`, {
        method: 'POST',
        body: JSON.stringify(narrativeInputDraft),
      })
      setRiskAnalysisNarrative(result.narrative)
      setNarrativeInputDraft(null)
      setMessage('Narrative draft generated. Review and Save when ready.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to generate the narrative.')
    } finally {
      setNarrativeGenerating(false)
    }
  }

  async function importCases(event: FormEvent<HTMLFormElement>, url: string) {
    event.preventDefault()
    try {
      setErrorMessage('')
      const form = new FormData(event.currentTarget)
      const response = await fetch(url, { method: 'POST', body: form })
      if (!response.ok) {
        const text = await response.text()
        throw new Error(text || 'Import failed.')
      }
      const result = (await response.json()) as ImportResult
      setImportResult(result)
      setImportFileName('No file selected.')
      setImportExcelFileName('No file selected.')
      event.currentTarget.reset()
      await refreshAll(selectedCaseId)
      setMessage('Import complete.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to import the file.')
    }
  }

  const selectedCase = workspace?.case ?? caseDraft
  function applyReportPreset(value: string) {
    setReportPreset(value)
    if (!value) return
    const today = new Date()
    const iso = (date: Date) => date.toISOString().slice(0, 10)
    const start = new Date(today)
    if (value === '30' || value === '90') start.setDate(today.getDate() - Number(value))
    if (value === '6m') start.setMonth(today.getMonth() - 6)
    if (value === '12m') start.setFullYear(today.getFullYear() - 1)
    if (value === 'thisYear') start.setMonth(0, 1)
    if (value === 'previousYear') { start.setFullYear(today.getFullYear() - 1, 0, 1); today.setFullYear(today.getFullYear() - 1, 11, 31) }
    setReportOpenedFrom(iso(start)); setReportOpenedTo(iso(today))
  }
  useEffect(() => {
    if (page !== 'reports') return
    const params = new URLSearchParams({ includeClosed: String(reportStatusFilter === '__closed'), status: reportStatusFilter === '__closed' ? '' : reportStatusFilter, county: reportCountyFilter, search: reportSearch, dateOpenedFrom: reportOpenedFrom, dateOpenedTo: reportOpenedTo })
    if (reportStatusFilter === '__closed') params.set('status', 'Closed')
    void api<CaseRecord[]>(`/api/cases?${params.toString()}`).then(setReportServerRows).catch(() => setReportServerRows([]))
  }, [page, reportStatusFilter, reportCountyFilter, reportSearch, reportOpenedFrom, reportOpenedTo])

  const reportRows = useMemo(() => {
    const query = reportSearch.trim().toLocaleLowerCase()
    const includeClosed = reportStatusFilter === '__closed'
    const rows = reportServerRows.filter((record) => {
      const status = record.caseStatus || 'Pipeline'
      if (!includeClosed && !['Pipeline', 'Filed / Service Pending', 'Active Litigation', 'Settlement Pending', 'Trial Preparation'].includes(status)) return false
      if (!includeClosed && record.status === 'Triage') return false
      if (includeClosed && ['Pipeline', 'Filed / Service Pending', 'Active Litigation', 'Settlement Pending', 'Trial Preparation'].includes(status)) return false
      if (reportStatusFilter && reportStatusFilter !== '__closed' && status !== reportStatusFilter) return false
      if (reportCountyFilter && record.county !== reportCountyFilter) return false
      if (reportOpenedFrom && (!record.dateOpened || record.dateOpened < reportOpenedFrom)) return false
      if (reportOpenedTo && (!record.dateOpened || record.dateOpened > reportOpenedTo)) return false
      if (query && ![record.caseName, record.caseNumber, record.jobNumber, record.tract, record.county, record.projectName].join(' ').toLocaleLowerCase().includes(query)) return false
      return true
    })
    return rows.sort((a, b) => {
      const left = reportCellValue(a, reportSortColumn).toLocaleLowerCase()
      const right = reportCellValue(b, reportSortColumn).toLocaleLowerCase()
      const comparison = left.localeCompare(right, undefined, { numeric: true })
      return reportSortDirection === 'asc' ? comparison : -comparison
    })
  }, [reportServerRows, reportStatusFilter, reportCountyFilter, reportOpenedFrom, reportOpenedTo, reportSearch, reportSortColumn, reportSortDirection])

  const reportMetrics = useMemo(() => {
    const closed = reportRows.filter((record) => Boolean(record.closedDate))
    const durations = closed.map((record) => lifecycleDays(record.dateOpened, record.closedDate)).filter((value): value is number => value != null)
    const open = reportRows.filter((record) => !record.closedDate)
    const ages = open.map((record) => lifecycleDays(record.dateOpened, null)).filter((value): value is number => value != null)
    const ageBands = { under90: ages.filter((value) => value < 90).length, days90to179: ages.filter((value) => value >= 90 && value < 180).length, days180to364: ages.filter((value) => value >= 180 && value < 365).length, year1to2: ages.filter((value) => value >= 365 && value < 730).length, year2to3: ages.filter((value) => value >= 730 && value < 1095).length, over3: ages.filter((value) => value >= 1095).length }
    const ordered = [...durations].sort((a, b) => a - b)
    const medianDuration = ordered.length ? (ordered.length % 2 ? ordered[(ordered.length - 1) / 2] : Math.round((ordered[ordered.length / 2 - 1] + ordered[ordered.length / 2]) / 2)) : null
    return { open: open.length, closed: closed.length, averageDuration: durations.length ? Math.round(durations.reduce((sum, value) => sum + value, 0) / durations.length) : null, medianDuration, shortestDuration: ordered[0] ?? null, longestDuration: ordered.at(-1) ?? null, averageAge: ages.length ? Math.round(ages.reduce((sum, value) => sum + value, 0) / ages.length) : null, missingDates: closed.length - durations.length, ageBands }
  }, [reportRows])

  function exportReportCsv() {
    const headers = reportColumns.map((column) => reportColumnOptions.find((option) => option.key === column)?.label ?? column)
    const escape = (value: string) => `"${value.replaceAll('"', '""')}"`
    const generated = new Date().toISOString()
    const filters = `Opened ${reportOpenedFrom || 'any'} to ${reportOpenedTo || 'any'}; Status ${reportStatusFilter || 'all'}`
    const csv = [['Case Report'], [`Generated: ${generated}`], [`Filters: ${filters}`], [], headers, ...reportRows.map((record) => reportColumns.map((column) => reportCellValue(record, column)))].map((row) => row.map(escape).join(',')).join('\r\n')
    const url = URL.createObjectURL(new Blob([`\uFEFF${csv}`], { type: 'text/csv;charset=utf-8' }))
    const link = document.createElement('a')
    link.href = url
    link.download = `Open_Case_Report_${new Date().toISOString().slice(0, 10)}.csv`
    link.click()
    URL.revokeObjectURL(url)
  }

  async function exportReportExcel() {
    const columns = reportColumns.map((key) => ({ key, label: reportColumnOptions.find((option) => option.key === key)?.label ?? key }))
    const rows = reportRows.map((record) => Object.fromEntries(reportColumns.map((column) => [column, reportCellValue(record, column)])))
    const generated = new Date().toISOString()
    const filters = { dateOpened: `${reportOpenedFrom || 'any'} to ${reportOpenedTo || 'any'}`, status: reportStatusFilter || 'all', county: reportCountyFilter || 'all' }
    const response = await fetch('/api/reports/export.xlsx', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ title: 'Case Lifecycle Report', generatedAt: generated, filters, fileName: `Open_Case_Report_${new Date().toISOString().slice(0, 10)}.xlsx`, columns, rows }) })
    if (!response.ok) throw new Error('Unable to export the Excel report.')
    const blob = await response.blob()
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `Open_Case_Report_${new Date().toISOString().slice(0, 10)}.xlsx`
    link.click()
    URL.revokeObjectURL(url)
  }

  async function exitCasePlanner() {
    if (shutdownBusy || !window.confirm('Exit Case Planner? Any request currently being saved or exported will be allowed to finish before the local server stops.')) return
    setShutdownBusy(true)
    try {
      const { token } = await api<{ token: string }>('/api/app/shutdown-token')
      await api('/api/app/shutdown', { method: 'POST', headers: { 'X-CasePlanner-Shutdown-Token': token } })
      setMessage('Case Planner is shutting down…')
    } catch (error) {
      setShutdownBusy(false)
      setErrorMessage(error instanceof Error ? error.message : 'Unable to shut down Case Planner.')
    }
  }
  const overviewWarnings = useMemo(() => {
    if (!workspace) return []
    const warnings: string[] = []
    if (workspace.serviceStatus.warningLevel && workspace.serviceStatus.warningLevel !== 'none' && workspace.serviceStatus.warningLevel !== 'resolved') warnings.push(workspace.serviceStatus.warningText)
    if (!discoveryPosture?.isComplete && workspace.discoveryItems.some((item) => item.status.includes('Follow-Up') || item.status.includes('Waiting'))) warnings.push('Discovery follow-up items need attention.')
    const today = new Date().toISOString().slice(0, 10)
    if (workspace.deadlines.some((item) => !isDeadlineDone(item) && item.dueDate && item.dueDate < today)) warnings.push('Overdue case deadlines need attention.')
    if (workspace.checklistItems.some((item) => !isChecklistDone(item) && item.dueDate && item.dueDate < today)) warnings.push('Overdue case tasks need attention.')
    return warnings
  }, [workspace, discoveryPosture])

  const openChecklistCount = workspace?.checklistItems.filter((item) => !isChecklistDone(item)).length ?? 0
  const openDiscoveryCount = workspace?.discoveryItems.filter((item) => item.status.includes('Waiting') || item.status.includes('Follow-Up')).length ?? 0
  const ashcValue = valuationPositions.find((p) => p.side === 'ASHC')?.appraisedValue ?? null
  const landownerValue = valuationPositions.find((p) => p.side === 'Landowner')?.appraisedValue ?? null
  const valuationGap = ashcValue != null && landownerValue != null ? landownerValue - ashcValue : null
  const acquisitionAcres = selectedCase.acquisitionAcres
  const gapPerAcre = valuationGap != null && acquisitionAcres ? valuationGap / acquisitionAcres : null
  const dashboardGreeting = useMemo(() => {
    const hour = new Date().getHours()
    if (hour < 12) return 'Good morning.'
    if (hour < 18) return 'Good afternoon.'
    return 'Good evening.'
  }, [])
  const dashboardDateLine = useMemo(() => new Date().toLocaleDateString(undefined, { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' }), [])
  const dashboardCasesById = useMemo(() => {
    const map = new Map<number, CaseRecord>()
    for (const item of allCases) map.set(item.id, item)
    for (const item of cases) map.set(item.id, item)
    for (const item of (dashboard?.attentionCases ?? [])) {
      if (!map.has(item.id)) map.set(item.id, item)
    }
    return map
  }, [allCases, cases, dashboard])
  // Metric-tile facet filter: a union of the selected priority levels; no tiles active shows everything.
  const filteredActionQueue = useMemo(() => {
    const queue = attorneyDashboard?.actionQueue ?? []
    if (activeQueueTiles.size === 0) return queue
    return queue.filter((item) => activeQueueTiles.has(item.priorityLevel))
  }, [attorneyDashboard, activeQueueTiles])
  function toggleQueueTile(level: number) {
    setActiveQueueTiles((current) => {
      const next = new Set(current)
      if (next.has(level)) next.delete(level)
      else next.add(level)
      return next
    })
  }
  const priorityQueueCounts = useMemo(() => {
    const counts: Record<number, number> = { 1: 0, 2: 0, 3: 0, 4: 0 }
    for (const item of attorneyDashboard?.actionQueue ?? []) counts[item.priorityLevel] = (counts[item.priorityLevel] ?? 0) + 1
    return counts
  }, [attorneyDashboard])
  const filteredServiceQueue = useMemo(() => serviceConditionFilter === 'missingDeadline'
    ? queueService.filter((item) => item.warningLevel === 'missing' || !item.serviceDeadline120)
    : serviceConditionFilter === 'notPerfected'
      ? queueService.filter((item) => !item.servicePerfected)
      : serviceConditionFilter === 'missingBasis'
        ? queueService.filter((item) => !item.serviceDeadlineBasisDate && !item.filingDate)
        : queueService, [queueService, serviceConditionFilter])
  const queueCaseName = (caseId: number) => allCases.find((item) => item.id === caseId)?.caseName || String(caseId)
  const queueSearchMatches = (caseId: number, extra = '') => { const query = workQueueSearch.trim().toLocaleLowerCase(); return !query || `${queueCaseName(caseId)} ${caseId} ${extra}`.toLocaleLowerCase().includes(query) }
  const visibleServiceQueue = useMemo(() => filteredServiceQueue.filter((item) => matchesUrgency(item.serviceDeadline120 || item.filingDate, workQueueUrgency) && queueSearchMatches(item.caseId, `${item.jobNumber || ''} ${item.tract || ''}`)), [filteredServiceQueue, workQueueUrgency, workQueueSearch, allCases])
  const sortedServiceQueue = useMemo(() => sortQueueItems(visibleServiceQueue, workQueueSort, (item) => item.caseName || item.caseNumber, (item) => item.serviceDeadline120 || item.filingDate), [visibleServiceQueue, workQueueSort])
  const sortedDeadlineQueue = useMemo(() => sortQueueItems(queueDeadlines.filter((item) => !isDeadlineDone(item) && matchesUrgency(item.dueDate, workQueueUrgency) && queueSearchMatches(item.caseId, item.title)), workQueueSort, (item) => queueCaseName(item.caseId), (item) => item.dueDate), [queueDeadlines, workQueueUrgency, workQueueSort, allCases, workQueueSearch])
  const sortedChecklistQueue = useMemo(() => sortQueueItems(queueChecklist.filter((item) => !isChecklistDone(item) && matchesUrgency(item.dueDate, workQueueUrgency) && queueSearchMatches(item.caseId, item.task)), workQueueSort, (item) => queueCaseName(item.caseId), (item) => item.dueDate), [queueChecklist, workQueueUrgency, workQueueSort, allCases, workQueueSearch])
  const sortedDiscoveryQueue = useMemo(() => sortQueueItems(queueDiscovery.filter((item) => matchesUrgency(item.followUpDate || item.dueDate, workQueueUrgency) && queueSearchMatches(item.caseId, item.requestTitle || item.discoveryType)), workQueueSort, (item) => queueCaseName(item.caseId), (item) => item.followUpDate || item.dueDate), [queueDiscovery, workQueueUrgency, workQueueSort, allCases, workQueueSearch])
  const sortedHearingQueue = useMemo(() => sortQueueItems(queueHearings.filter((item) => matchesUrgency(item.hearingDate, workQueueUrgency) && queueSearchMatches(item.caseId, item.title)), workQueueSort, (item) => queueCaseName(item.caseId), (item) => item.hearingDate), [queueHearings, workQueueUrgency, workQueueSort, allCases, workQueueSearch])
  // Raw eligible-work pipeline (all types, no urgency/limit narrowing) - the dashboard's "Due in the
  // next 7 days" panel and the Work Queue page both read from this; the Work Queue page owns its own
  // type/urgency/search filtering separately (workQueueFilter etc.), so this stays unfiltered here.
  const upcomingWorkItems = useMemo(() => {
    const today = new Date().toISOString().slice(0, 10)
    const caseById = new Map(allCases.map((item) => [item.id, item]))
    const eligible = (caseId: number, type: UpcomingWorkType) => {
      const record = caseById.get(caseId)
      if (!record || record.caseStatus === 'Resolved / Closed' || record.caseStatus === 'Triage' || record.status === 'Closed' || record.status === 'Triage') return false
      if (record.deferredUntil && record.deferredUntil > today) return false
      if ((record.caseStatus || 'Pipeline') === 'Pipeline' && type !== 'service') return false
      return true
    }
    const items: UpcomingWorkItem[] = []
    for (const item of queueChecklist) if (!isChecklistDone(item) && eligible(item.caseId, 'task')) items.push({ key: `task-${item.id}`, caseId: item.caseId, caseName: queueCaseName(item.caseId), title: item.task, type: 'task', dueDate: item.dueDate, source: item, tab: 'work' })
    for (const item of queueDeadlines) if (!isDeadlineDone(item) && eligible(item.caseId, 'deadline')) items.push({ key: `deadline-${item.id}`, caseId: item.caseId, caseName: queueCaseName(item.caseId), title: item.title, type: 'deadline', dueDate: item.dueDate, source: item, tab: 'work' })
    for (const item of queueDiscovery) if (!item.status.toLowerCase().includes('complete') && !item.status.toLowerCase().includes('cancel') && eligible(item.caseId, 'discovery')) items.push({ key: `discovery-${item.id}`, caseId: item.caseId, caseName: queueCaseName(item.caseId), title: item.requestTitle || `${item.direction} ${item.discoveryType}`, type: 'discovery', dueDate: item.followUpDate || item.dueDate, source: item, tab: 'discovery' })
    for (const item of queueService) if (!item.servicePerfected && eligible(item.caseId, 'service')) items.push({ key: `service-${item.caseId}`, caseId: item.caseId, caseName: item.caseName, title: item.serviceDeadline120 ? 'Perfect service' : 'Complete service record', type: 'service', dueDate: item.serviceDeadline120 || item.filingDate, source: item, tab: 'servicePublication' })
    for (const item of queueHearings) if (eligible(item.caseId, 'hearing')) items.push({ key: `hearing-${item.id}`, caseId: item.caseId, caseName: queueCaseName(item.caseId), title: item.title, type: 'hearing', dueDate: item.hearingDate, source: item, tab: 'work' })
    return items.sort((a, b) => {
      const dueA = a.dueDate || '9999-12-31'
      const dueB = b.dueDate || '9999-12-31'
      const urgencyA = !a.dueDate ? 5 : dueA < today ? 0 : dueA === today ? 1 : DateOnlyFromString(dueA)! - DateOnlyFromString(today)! <= 7 ? 2 : DateOnlyFromString(dueA)! - DateOnlyFromString(today)! <= 14 ? 3 : 4
      const urgencyB = !b.dueDate ? 5 : dueB < today ? 0 : dueB === today ? 1 : DateOnlyFromString(dueB)! - DateOnlyFromString(today)! <= 7 ? 2 : DateOnlyFromString(dueB)! - DateOnlyFromString(today)! <= 14 ? 3 : 4
      return urgencyA - urgencyB || dueA.localeCompare(dueB) || a.caseName.localeCompare(b.caseName)
    })
  }, [queueChecklist, queueDeadlines, queueDiscovery, queueService, queueHearings, allCases])
  useEffect(() => {
    let cancelled = false
    // Fixed "all open" fetch - the dashboard no longer exposes type/urgency/limit controls, so the
    // window is narrowed client-side (see dashboardDueThisWeekItems below) after loading.
    const params = new URLSearchParams({ type: 'all', urgency: 'All Open', limit: '200' })
    void api<Array<Omit<UpcomingWorkItem, 'source'>>>(`/api/dashboard/upcoming-work?${params.toString()}`)
      .then((items) => { if (!cancelled) { setServerUpcomingWorkItems(items.map((item) => ({ ...item, tab: normalizeUpcomingWorkTab(item.tab, item.type) }))); setServerUpcomingWorkLoaded(true) } })
      .catch(() => { if (!cancelled) setServerUpcomingWorkLoaded(false) })
    return () => { cancelled = true }
  }, [])
  const dashboardUpcomingWorkItems = serverUpcomingWorkLoaded ? serverUpcomingWorkItems : upcomingWorkItems
  // "Due in the next 7 days" window: overdue items stay visible so nothing urgent silently drops
  // off the dashboard once its due date passes.
  const dashboardDueThisWeekItems = useMemo(() => {
    const today = DateOnlyFromString(new Date().toISOString().slice(0, 10))!
    return dashboardUpcomingWorkItems.filter((item) => item.dueDate != null && DateOnlyFromString(item.dueDate)! - today <= 7)
  }, [dashboardUpcomingWorkItems])
  // Headline strip: "N active cases · X need action now · Y due this week" - N comes from the
  // (non-attorney) dashboard summary, X is the Immediate-priority action-queue count, Y is the
  // "Due in the next 7 days" panel's item count.
  const dashboardHeadline = useMemo(() => ({
    activeCaseCount: dashboard?.activeCaseCount ?? null,
    actionsNeededNow: priorityQueueCounts[1] ?? 0,
    dueThisWeekCount: dashboardDueThisWeekItems.length,
  }), [dashboard, priorityQueueCounts, dashboardDueThisWeekItems])
  const workQueueFilteredCount = useMemo(() => {
    const service = sortedServiceQueue.length
    const deadlines = sortedDeadlineQueue.length
    const tasks = sortedChecklistQueue.length
    const discovery = sortedDiscoveryQueue.length
    const hearings = sortedHearingQueue.length
    return workQueueFilter === 'service' ? service : workQueueFilter === 'deadlines' ? deadlines : workQueueFilter === 'checklist' ? tasks : workQueueFilter === 'discovery' ? discovery : workQueueFilter === 'hearings' ? hearings : service + deadlines + tasks + discovery + hearings
  }, [sortedServiceQueue, sortedDeadlineQueue, sortedChecklistQueue, sortedDiscoveryQueue, sortedHearingQueue, workQueueFilter])
  const docketCases = useMemo(() => {
    if (!docketMetricFilter) return []
    return allCases.filter((c) => {
      const pipeline = (c.caseStatus || 'Pipeline') === 'Pipeline'
      switch (docketMetricFilter) {
        case 'preFiling': return pipeline
        case 'filed': return !pipeline && c.caseStatus !== 'Triage' && c.caseStatus !== 'Resolved / Closed'
        case 'trial': return Boolean(c.trialDate) || c.trialTrack === true
        case 'waiting': return pipeline && (c.currentHolder || '') !== 'Attorney'
        case 'desk': return pipeline && (c.currentHolder || '') === 'Attorney'
        case 'missingReview': return pipeline && !c.nextReviewDate
      }
    })
  }, [allCases, docketMetricFilter])

  function renderCaseListPage() {
    return (
      <main className="page">
        <section className="hero-panel">
          <div>
            <p className="eyebrow dark">Case Management</p>
            <h2>Cases</h2>
            <p className="subtle-text">Browse active matters, filter the list, and open a case workspace with deadlines, checklist, discovery, documents, service details, and issue tags in one place.</p>
          </div>
          <div className="button-row compact-actions">
            <button className="primary" onClick={startNewCase}>Add Case</button>
            <button onClick={() => openSettingsSection('import')}>Import Cases</button>
          </div>
        </section>

        {allCases.some((c) => c.status === 'Triage') && caseStatusFilter !== 'Triage' && (
          <div className="inline-message warn">
            {allCases.filter((c) => c.status === 'Triage').length} imported case{allCases.filter((c) => c.status === 'Triage').length === 1 ? '' : 's'} awaiting triage — no alerts are generated until intake is completed.
            <button style={{ marginLeft: '0.75rem' }} onClick={() => goToTriageQueue()}>Review</button>
          </div>
        )}

        <Panel title="Case Filters">
          <div className="chip-row">
            <button className={caseStatusFilter === '' ? 'chip active' : 'chip'} onClick={() => { setCaseStatusFilter(''); void loadCasesWithOverride({ caseStatus: '' }) }}>All statuses</button>
            {consolidatedCaseStatuses.map((status) => (
              <button key={status} className={caseStatusFilter === status ? 'chip active' : 'chip'} onClick={() => { setCaseStatusFilter(status); void loadCasesWithOverride({ caseStatus: status }) }}>
                {status}
              </button>
            ))}
          </div>
          <div className="filters-grid top-gap-small">
            <label>
              <span>Search</span>
              <input value={caseSearch} onChange={(event) => setCaseSearch(event.target.value)} placeholder="Case name, number, job, or tract" />
            </label>
            <label>
              <span>County</span>
              <select value={countyFilter} onChange={(event) => { setCountyFilter(event.target.value); void loadCasesWithOverride({ county: event.target.value }) }}>
                <option value="">Select county</option>
                {countyOptions(countyFilter).map((county) => (
                  <option key={county} value={county}>{county}</option>
                ))}
              </select>
            </label>
            <label className="toggle-card">
              <span>Include Closed</span>
              <input type="checkbox" checked={includeClosed} onChange={(event) => { setIncludeClosed(event.target.checked); void loadCasesWithOverride({ includeClosed: event.target.checked }) }} />
            </label>
          </div>
          <div className="button-row compact-actions top-gap">
            <button onClick={() => void loadCases()}>Apply Filters</button>
            <button onClick={() => { setCaseSearch(''); setStatusFilter(''); setCaseStatusFilter(''); setCountyFilter(''); setIncludeClosed(false); void loadInitial() }}>Clear Filters</button>
          </div>
          <label className="toggle-inline top-gap-small"><span>Show lifecycle dates</span><input type="checkbox" checked={caseListShowLifecycle} onChange={(event) => setCaseListShowLifecycle(event.target.checked)} /></label>
          <p className="helper-text top-gap-small">Case Status, County, and Include Closed apply immediately. Search needs Apply Filters. Legacy stage and track values remain available inside each case for historical context.</p>
        </Panel>

        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                {([
                  { key: 'caseName', label: 'Case' },
                  { key: 'jobNumber', label: 'Job' },
                  { key: 'tract', label: 'Tract' },
                  { key: 'county', label: 'County' },
                  { key: 'nextDeadlineDate', label: 'Next Deadline' },
                  { key: 'attentionStatus', label: 'Status' },
                  ...(caseListShowLifecycle ? [{ key: 'dateOpened', label: 'Date Opened' }, { key: 'closedDate', label: 'Date Closed' }] : []),
                ] as { key: CaseSortColumn; label: string }[]).map((column) => (
                  <th key={column.key} className="sortable-header" onClick={() => toggleCaseSort(column.key)}>
                    {column.label}
                    {caseSortColumn === column.key && <span className="sort-indicator">{caseSortDirection === 'asc' ? ' ▲' : ' ▼'}</span>}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {cases.length === 0 ? (
                <tr><td colSpan={caseListShowLifecycle ? 8 : 6}>No cases match the current filters.</td></tr>
              ) : (caseSortColumn ? sortCases(cases, caseSortColumn, caseSortDirection) : cases).map((item) => (
                <tr key={item.id} className="clickable-row" onClick={() => openCase(item.id, 'overview')}>
                  <td>
                    <button className="link-button" onClick={(event) => { event.stopPropagation(); openCase(item.id, 'overview') }}>{item.caseName}</button>
                    <div className="flag-text muted">{item.caseNumber}</div>
                  </td>
                  <td>{item.jobNumber || 'Not set'}</td>
                  <td>{item.tract || 'Not set'}</td>
                  <td>{item.county || 'Not set'}</td>
                  <td>
                    {item.nextDeadlineDate ? (
                      <>
                        <div>{displayDate(item.nextDeadlineDate)}</div>
                        <div className="flag-text muted">{item.nextDeadlineTitle}</div>
                      </>
                    ) : 'None'}
                  </td>
                  <td><span className={`pill pill-${attentionPillTone(item.attentionStatus)}`}>{attentionLabels[item.attentionStatus || 'onTrack']}</span></td>
                  {caseListShowLifecycle && <><td>{displayDate(item.dateOpened)}</td><td>{displayDate(item.closedDate)}</td></>}
                </tr>
              ))}
            </tbody>
          </table>
        </div>

      </main>
    )
  }

  function renderWorkQueuePage() {
    const queueRowCaseName = (row: QueueRow): string =>
      row.kind === 'service' ? (row.item.caseName || row.item.caseNumber) : (dashboardCasesById.get(row.item.caseId)?.caseName || `Case ${row.item.caseId}`)
    const queueRowDue = (row: QueueRow): string | null | undefined => {
      switch (row.kind) {
        case 'service': return row.item.serviceDeadline120 || row.item.filingDate
        case 'deadline': return row.item.dueDate
        case 'task': return row.item.dueDate
        case 'discovery': return row.item.followUpDate || row.item.dueDate
        case 'event': return row.item.hearingDate
      }
    }
    const renderCaseCell = (caseId: number, tab: CaseTabKey) => {
      const record = dashboardCasesById.get(caseId)
      return (
        <td>
          <button className="ui-case-link" onClick={() => openCase(caseId, tab)}>{record?.caseName || `Case ${caseId}`}</button>
          {record?.caseNumber && <div className="ui-sub ui-data">{record.caseNumber}</div>}
        </td>
      )
    }

    const serviceRows: QueueRow[] = sortedServiceQueue.map((item) => ({ kind: 'service', key: `service-${item.caseId}`, item }))
    const deadlineRows: QueueRow[] = sortedDeadlineQueue.map((item) => ({ kind: 'deadline', key: `deadline-${item.id}`, item }))
    const taskRows: QueueRow[] = sortedChecklistQueue.map((item) => ({ kind: 'task', key: `task-${item.id}`, item }))
    const discoveryRows: QueueRow[] = sortedDiscoveryQueue.map((item) => ({ kind: 'discovery', key: `discovery-${item.id}`, item }))
    const eventRows: QueueRow[] = sortedHearingQueue.map((item) => ({ kind: 'event', key: `event-${item.id}`, item }))

    const facetRows: QueueRow[] =
      workQueueFilter === 'service' ? serviceRows
      : workQueueFilter === 'deadlines' ? deadlineRows
      : workQueueFilter === 'checklist' ? taskRows
      : workQueueFilter === 'discovery' ? discoveryRows
      : workQueueFilter === 'hearings' ? eventRows
      : sortQueueItems([...serviceRows, ...deadlineRows, ...taskRows, ...discoveryRows, ...eventRows], workQueueSort, queueRowCaseName, queueRowDue)

    const totalDeadlinesOpen = queueDeadlines.filter((item) => !isDeadlineDone(item)).length
    const totalTasksOpen = queueChecklist.filter((item) => !isChecklistDone(item)).length
    const allOpenItemsCount = queueService.length + totalDeadlinesOpen + totalTasksOpen + queueDiscovery.length + queueHearings.length

    const totalForFacet =
      workQueueFilter === 'service' ? filteredServiceQueue.length
      : workQueueFilter === 'deadlines' ? totalDeadlinesOpen
      : workQueueFilter === 'checklist' ? totalTasksOpen
      : workQueueFilter === 'discovery' ? queueDiscovery.length
      : workQueueFilter === 'hearings' ? queueHearings.length
      : filteredServiceQueue.length + totalDeadlinesOpen + totalTasksOpen + queueDiscovery.length + queueHearings.length

    const clearFilters = () => { setWorkQueueUrgency('All Open'); setWorkQueueFilter('all'); setWorkQueueSearch(''); setServiceConditionFilter('all') }

    const typeFacets: { key: typeof workQueueFilter; label: string; count: number }[] = [
      { key: 'all', label: 'All', count: sortedServiceQueue.length + sortedDeadlineQueue.length + sortedChecklistQueue.length + sortedDiscoveryQueue.length + sortedHearingQueue.length },
      { key: 'service', label: 'Service', count: sortedServiceQueue.length },
      { key: 'deadlines', label: 'Deadlines', count: sortedDeadlineQueue.length },
      { key: 'checklist', label: 'Tasks', count: sortedChecklistQueue.length },
      { key: 'discovery', label: 'Discovery', count: sortedDiscoveryQueue.length },
      { key: 'hearings', label: 'Events', count: sortedHearingQueue.length },
    ]
    const serviceConditionChips: { key: typeof serviceConditionFilter; label: string }[] = [
      { key: 'all', label: 'All conditions' },
      { key: 'missingDeadline', label: 'Missing deadline' },
      { key: 'notPerfected', label: 'Not perfected' },
      { key: 'missingBasis', label: 'Missing basis date' },
    ]

    // ---- single-facet extra tables (bulk selection + facet-specific columns) ----

    const deadlineAllSelected = sortedDeadlineQueue.length > 0 && sortedDeadlineQueue.every((item) => selectedDeadlineIds.includes(item.id))
    const deadlineSelectedCount = sortedDeadlineQueue.filter((item) => selectedDeadlineIds.includes(item.id)).length
    const renderDeadlinesFacetTable = () => (
      <div className="ui-table-panel">
        <div className="bulk-action-bar">
          <div className="bulk-action-summary">
            <label className="bulk-select-all">
              <input type="checkbox" checked={deadlineAllSelected} onChange={(event) => setAllSelectedDeadlines(sortedDeadlineQueue, event.target.checked)} aria-label="Select all visible deadlines" />
              <span>Select all visible</span>
            </label>
            <span className="helper-text">{deadlineSelectedCount} selected</span>
          </div>
          <div className="bulk-action-controls">
            <button onClick={() => void applyBulkDeadlineAction('complete', sortedDeadlineQueue)} disabled={deadlineSelectedCount === 0}>Mark Done</button>
            <button onClick={() => void applyBulkDeadlineAction('reopen', sortedDeadlineQueue)} disabled={deadlineSelectedCount === 0}>Reopen</button>
            <input type="date" value={bulkDeadlineDueDate} onChange={(event) => setBulkDeadlineDueDate(event.target.value)} disabled={deadlineSelectedCount === 0} />
            <button onClick={() => void applyBulkDeadlineAction('dueDate', sortedDeadlineQueue)} disabled={deadlineSelectedCount === 0 || !bulkDeadlineDueDate}>Apply Due Date</button>
            <button onClick={() => setSelectedDeadlineIds([])} disabled={deadlineSelectedCount === 0}>Clear</button>
          </div>
        </div>
        <div className="table-wrap">
          <table className="ui-table">
            <thead>
              <tr>
                <th className="selection-cell">Select</th>
                <th style={{ width: 95 }}>Type</th>
                <th>Item</th>
                <th>Case</th>
                <th style={{ width: 150 }}>Due</th>
                <th style={{ width: 170 }}>Status</th>
                <th style={{ width: 120 }}>Severity</th>
                <th style={{ width: 170 }}></th>
              </tr>
            </thead>
            <tbody>
              {sortedDeadlineQueue.length === 0 ? (
                <UiEmptyState colSpan={8} title="No deadlines match the current filters" hint="Try a different urgency, or clear all filters." action={<Btn size="sm" onClick={clearFilters}>Clear filters</Btn>} />
              ) : sortedDeadlineQueue.map((item) => (
                <tr key={item.id}>
                  <td className="selection-cell">
                    <input type="checkbox" checked={selectedDeadlineIds.includes(item.id)} onChange={() => toggleSelectedDeadline(item.id)} aria-label={`Select deadline ${item.title}`} />
                  </td>
                  <td><TypeChip kind="deadline" /></td>
                  <td>
                    {item.title}
                    {item.completedAt && <div className="ui-sub">Completed {displayDateTime(item.completedAt)}</div>}
                    <div className="ui-sub">Source: {item.sourceKind || item.sourceType}{item.sourceStage ? ` · ${item.sourceStage}` : ''}</div>
                  </td>
                  {renderCaseCell(item.caseId, 'work')}
                  <td>
                    <input type="date" className="inline-edit-input" value={item.dueDate || ''} onChange={(event) => void persistDeadline({ ...item, dueDate: event.target.value }, 'Due date updated.', false)} />
                  </td>
                  <td>
                    <StatusSelect value={item.status} options={deadlineStatuses} tone={deadlineRowTone(item)} ariaLabel={`Status for ${item.title}`} onChange={(value) => void persistDeadline({ ...item, status: value }, 'Deadline status updated.', false)} />
                  </td>
                  <td>
                    <select className="inline-edit-select" value={item.severity || 'normal'} onChange={(event) => void persistDeadline({ ...item, severity: event.target.value }, 'Deadline severity updated.', false)}>
                      {deadlineSeverities.map((level) => <option key={level} value={level}>{level}</option>)}
                    </select>
                  </td>
                  <td>
                    <div className="ui-row-actions">
                      {!isDeadlineDone(item) && <Btn size="sm" onClick={() => void persistDeadline({ ...item, status: 'Done' }, 'Deadline marked done.', false)}>Mark done</Btn>}
                      <Btn size="sm" variant="ghost" onClick={() => openCase(item.caseId, 'work')}>Open case ▸</Btn>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    )

    const taskAllSelected = sortedChecklistQueue.length > 0 && sortedChecklistQueue.every((item) => selectedChecklistIds.includes(item.id))
    const taskSelectedCount = sortedChecklistQueue.filter((item) => selectedChecklistIds.includes(item.id)).length
    const renderTasksFacetTable = () => (
      <div className="ui-table-panel">
        <div className="bulk-action-bar">
          <div className="bulk-action-summary">
            <label className="bulk-select-all">
              <input type="checkbox" checked={taskAllSelected} onChange={(event) => setAllSelectedChecklist(sortedChecklistQueue, event.target.checked)} aria-label="Select all visible tasks" />
              <span>Select all visible</span>
            </label>
            <span className="helper-text">{taskSelectedCount} selected</span>
          </div>
          <div className="bulk-action-controls">
            <button onClick={() => void applyBulkChecklistAction('complete', sortedChecklistQueue)} disabled={taskSelectedCount === 0}>Mark Done</button>
            <button onClick={() => void applyBulkChecklistAction('reopen', sortedChecklistQueue)} disabled={taskSelectedCount === 0}>Reopen</button>
            <button onClick={() => setBulkChecklistDueDateOpen((open) => !open)} disabled={taskSelectedCount === 0}>Change Due Date</button>
            {bulkChecklistDueDateOpen && <span className="bulk-date-popover"><input type="date" value={bulkChecklistDueDate} onChange={(event) => setBulkChecklistDueDate(event.target.value)} autoFocus /><button onClick={() => { setBulkChecklistDueDateOpen(false); void applyBulkChecklistAction('dueDate', sortedChecklistQueue) }} disabled={!bulkChecklistDueDate}>Apply</button><button onClick={() => setBulkChecklistDueDateOpen(false)}>Cancel</button></span>}
            <button onClick={() => setSelectedChecklistIds([])} disabled={taskSelectedCount === 0}>Clear</button>
          </div>
        </div>
        <div className="table-wrap">
          <table className="ui-table">
            <thead>
              <tr>
                <th className="selection-cell">Select</th>
                <th style={{ width: 95 }}>Type</th>
                <th>Item</th>
                <th>Case</th>
                <th style={{ width: 150 }}>Due</th>
                <th style={{ width: 170 }}>Status</th>
                <th style={{ width: 170 }}></th>
              </tr>
            </thead>
            <tbody>
              {sortedChecklistQueue.length === 0 ? (
                <UiEmptyState colSpan={7} title="No tasks match the current filters" hint="Try a different urgency, or clear all filters." action={<Btn size="sm" onClick={clearFilters}>Clear filters</Btn>} />
              ) : sortedChecklistQueue.map((item) => (
                <tr key={item.id}>
                  <td className="selection-cell">
                    <input type="checkbox" checked={selectedChecklistIds.includes(item.id)} onChange={() => toggleSelectedChecklist(item.id)} aria-label={`Select task ${item.task}`} />
                  </td>
                  <td><TypeChip kind="task" /></td>
                  <td>
                    {item.task}
                    <div className="ui-sub">{item.phase || 'General'}</div>
                    {item.completedAt && <div className="ui-sub">Completed {displayDateTime(item.completedAt)}</div>}
                  </td>
                  {renderCaseCell(item.caseId, 'work')}
                  <td>
                    <input type="date" className="inline-edit-input" value={item.dueDate || ''} onChange={(event) => void persistChecklist({ ...item, dueDate: event.target.value }, 'Due date updated.', false)} />
                  </td>
                  <td>
                    <StatusSelect value={item.status} options={checklistStatuses} tone={checklistRowTone(item)} ariaLabel={`Status for ${item.task}`} onChange={(value) => void persistChecklist({ ...item, status: value }, 'Checklist status updated.', false)} />
                  </td>
                  <td>
                    <div className="ui-row-actions">
                      {!isChecklistDone(item) && <Btn size="sm" onClick={() => void persistChecklist({ ...item, status: 'Done' }, 'Task marked done.', false)}>Mark done</Btn>}
                      <Btn size="sm" variant="ghost" onClick={() => openCase(item.caseId, 'work')}>Open case ▸</Btn>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    )

    const renderServiceFacetTable = () => (
      <div className="ui-table-panel">
        <div className="table-wrap">
          <table className="ui-table">
            <thead>
              <tr>
                <th style={{ width: 95 }}>Type</th>
                <th>Item</th>
                <th>Case</th>
                <th style={{ width: 120 }}>Basis date</th>
                <th style={{ width: 135 }}>Due</th>
                <th style={{ width: 120 }}>Perfected</th>
                <th style={{ width: 160 }}>Method</th>
                <th style={{ width: 170 }}>Status</th>
                <th style={{ width: 170 }}></th>
              </tr>
            </thead>
            <tbody>
              {sortedServiceQueue.length === 0 ? (
                <UiEmptyState colSpan={9} title="No service items match the current filters" hint="Try a different urgency or service condition." action={<Btn size="sm" onClick={clearFilters}>Clear filters</Btn>} />
              ) : sortedServiceQueue.map((item) => (
                <tr key={`${item.caseId}-${item.caseNumber}`}>
                  <td><TypeChip kind="service" /></td>
                  <td>
                    120-day service deadline
                    {item.warningText && <div className="ui-sub">{item.warningText}</div>}
                  </td>
                  <td>
                    <button className="ui-case-link" onClick={() => openCase(item.caseId, 'servicePublication')}>{item.caseName}</button>
                    <div className="ui-sub ui-data">{item.caseNumber}</div>
                  </td>
                  <td className="ui-data">{displayDate(item.serviceDeadlineBasisDate || item.filingDate)}</td>
                  <td className={`ui-data${isQueueDateOverdue(item.serviceDeadline120) && !item.servicePerfected ? ' ui-cell-danger' : ''}`}>{displayDate(item.serviceDeadline120)}</td>
                  <td className="ui-data">{item.servicePerfected ? displayDate(item.servicePerfectedDate) : '—'}</td>
                  <td>{item.serviceMethod || '—'}{item.serviceStatus ? ` · ${item.serviceStatus}` : ''}</td>
                  <td>
                    {item.servicePerfected ? (
                      <StatusChip tone="ok">Perfected {displayDate(item.servicePerfectedDate)}</StatusChip>
                    ) : (
                      <StatusChip tone={serviceWarningTone(item.warningLevel)}>Not perfected</StatusChip>
                    )}
                  </td>
                  <td>
                    <div className="ui-row-actions">
                      {!item.servicePerfected && <Btn size="sm" onClick={() => void markGlobalServicePerfected(item.caseId)}>Mark perfected</Btn>}
                      <Btn size="sm" variant="ghost" onClick={() => openCase(item.caseId, 'servicePublication')}>Open case ▸</Btn>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    )

    // ---- unified table (facets: all / discovery / hearings, and the merged "all" view) ----

    const renderUnifiedRow = (row: QueueRow) => {
      switch (row.kind) {
        case 'service': {
          const item = row.item
          return (
            <tr key={row.key}>
              <td><TypeChip kind="service" /></td>
              <td>
                120-day service deadline
                {item.warningText && <div className="ui-sub">{item.warningText}</div>}
              </td>
              <td>
                <button className="ui-case-link" onClick={() => openCase(item.caseId, 'servicePublication')}>{item.caseName}</button>
                <div className="ui-sub ui-data">{item.caseNumber}</div>
              </td>
              <td className={`ui-data${isQueueDateOverdue(item.serviceDeadline120) && !item.servicePerfected ? ' ui-cell-danger' : ''}`}>{displayDate(item.serviceDeadline120)}</td>
              <td>
                {item.servicePerfected ? (
                  <StatusChip tone="ok">Perfected {displayDate(item.servicePerfectedDate)}</StatusChip>
                ) : (
                  <StatusChip tone={serviceWarningTone(item.warningLevel)}>Not perfected</StatusChip>
                )}
              </td>
              <td>
                <div className="ui-row-actions">
                  {!item.servicePerfected && <Btn size="sm" onClick={() => void markGlobalServicePerfected(item.caseId)}>Mark perfected</Btn>}
                  <Btn size="sm" variant="ghost" onClick={() => openCase(item.caseId, 'servicePublication')}>Open case ▸</Btn>
                </div>
              </td>
            </tr>
          )
        }
        case 'deadline': {
          const item = row.item
          return (
            <tr key={row.key}>
              <td><TypeChip kind="deadline" /></td>
              <td>
                {item.title}
                {item.completedAt && <div className="ui-sub">Completed {displayDateTime(item.completedAt)}</div>}
                <div className="ui-sub">Source: {item.sourceKind || item.sourceType}{item.sourceStage ? ` · ${item.sourceStage}` : ''}</div>
              </td>
              {renderCaseCell(item.caseId, 'work')}
              <td className={`ui-data${isQueueDateOverdue(item.dueDate) ? ' ui-cell-danger' : ''}`}>{displayDate(item.dueDate)}</td>
              <td>
                <StatusSelect value={item.status} options={deadlineStatuses} tone={deadlineRowTone(item)} ariaLabel={`Status for ${item.title}`} onChange={(value) => void persistDeadline({ ...item, status: value }, 'Deadline status updated.', false)} />
              </td>
              <td>
                <div className="ui-row-actions">
                  {!isDeadlineDone(item) && <Btn size="sm" onClick={() => void persistDeadline({ ...item, status: 'Done' }, 'Deadline marked done.', false)}>Mark done</Btn>}
                  <Btn size="sm" variant="ghost" onClick={() => openCase(item.caseId, 'work')}>Open case ▸</Btn>
                </div>
              </td>
            </tr>
          )
        }
        case 'task': {
          const item = row.item
          return (
            <tr key={row.key}>
              <td><TypeChip kind="task" /></td>
              <td>
                {item.task}
                <div className="ui-sub">{item.phase || 'General'}</div>
                {item.completedAt && <div className="ui-sub">Completed {displayDateTime(item.completedAt)}</div>}
              </td>
              {renderCaseCell(item.caseId, 'work')}
              <td className={`ui-data${isQueueDateOverdue(item.dueDate) ? ' ui-cell-danger' : ''}`}>{displayDate(item.dueDate)}</td>
              <td>
                <StatusSelect value={item.status} options={checklistStatuses} tone={checklistRowTone(item)} ariaLabel={`Status for ${item.task}`} onChange={(value) => void persistChecklist({ ...item, status: value }, 'Checklist status updated.', false)} />
              </td>
              <td>
                <div className="ui-row-actions">
                  {!isChecklistDone(item) && <Btn size="sm" onClick={() => void persistChecklist({ ...item, status: 'Done' }, 'Task marked done.', false)}>Mark done</Btn>}
                  <Btn size="sm" variant="ghost" onClick={() => openCase(item.caseId, 'work')}>Open case ▸</Btn>
                </div>
              </td>
            </tr>
          )
        }
        case 'discovery': {
          const item = row.item
          return (
            <tr key={row.key}>
              <td><TypeChip kind="discovery" /></td>
              <td>
                {item.requestTitle || `${item.direction} ${item.discoveryType}`}
                <div className="ui-sub">{item.direction}{item.servedDate ? ` · served ${displayDate(item.servedDate)}` : ''}</div>
              </td>
              {renderCaseCell(item.caseId, 'discovery')}
              <td className={`ui-data${isQueueDateOverdue(item.followUpDate || item.dueDate) ? ' ui-cell-danger' : ''}`}>{displayDate(item.followUpDate || item.dueDate)}</td>
              <td>
                <StatusSelect value={item.status} options={discoveryStatuses} tone={discoveryStatusTone(item.status)} ariaLabel={`Status for ${item.requestTitle || item.discoveryType}`} onChange={(value) => void persistDiscovery({ ...item, status: value }, 'Discovery status updated.', false)} />
              </td>
              <td>
                <div className="ui-row-actions">
                  <Btn size="sm" onClick={() => void recordDiscoveryResponse(item)}>Record response</Btn>
                  <Btn size="sm" variant="ghost" onClick={() => openCase(item.caseId, 'discovery')}>Open case ▸</Btn>
                </div>
              </td>
            </tr>
          )
        }
        case 'event': {
          const item = row.item
          return (
            <tr key={row.key}>
              <td><TypeChip kind="event" /></td>
              <td>
                {item.title}
                {item.location && <div className="ui-sub">{item.location}</div>}
              </td>
              {renderCaseCell(item.caseId, 'work')}
              <td className="ui-data">{displayDate(item.hearingDate)}</td>
              <td><StatusChip tone="neutral">Scheduled</StatusChip></td>
              <td>
                <div className="ui-row-actions">
                  <Btn size="sm" variant="ghost" onClick={() => openCase(item.caseId, 'work')}>Open case ▸</Btn>
                </div>
              </td>
            </tr>
          )
        }
      }
    }

    const renderUnifiedTable = () => (
      <div className="ui-table-panel">
        <div className="table-wrap">
          <table className="ui-table">
            <thead>
              <tr>
                <th style={{ width: 95 }}>Type</th>
                <th>Item</th>
                <th>Case</th>
                <th style={{ width: 135 }}>Due</th>
                <th style={{ width: 180 }}>Status</th>
                <th style={{ width: 200 }}></th>
              </tr>
            </thead>
            <tbody>
              {facetRows.length === 0 ? (
                <UiEmptyState colSpan={6} title="No items match the current filters" hint="Try a different urgency or item type, or clear all filters." action={<Btn size="sm" onClick={clearFilters}>Clear filters</Btn>} />
              ) : facetRows.map((row) => renderUnifiedRow(row))}
            </tbody>
          </table>
        </div>
      </div>
    )

    return (
      <main className="page">
        <div className="queue-title-row">
          <h2>Work Queue</h2>
          <span className="muted">every open item across all cases · {allOpenItemsCount} item{allOpenItemsCount === 1 ? '' : 's'}</span>
        </div>

        <FilterBar>
          {typeFacets.map((facet) => (
            <FilterChip key={facet.key} active={workQueueFilter === facet.key} onClick={() => setWorkQueueFilter(facet.key)}>
              {facet.label} · {facet.count}
            </FilterChip>
          ))}
          <FilterSep />
          <select aria-label="Urgency" value={workQueueUrgency} onChange={(event) => setWorkQueueUrgency(event.target.value)}>
            {['All Open', 'Overdue', 'Due Today', 'Due in 7 Days', 'Due in 14 Days', 'Due in 30 Days', 'No Due Date'].map((option) => <option key={option}>{option}</option>)}
          </select>
          <select aria-label="Sort" value={workQueueSort} onChange={(event) => setWorkQueueSort(event.target.value as QueueSortMode)}>
            <option value="dueAsc">Due date ↑</option>
            <option value="dueDesc">Due date ↓</option>
            <option value="caseAsc">Case A–Z</option>
            <option value="caseDesc">Case Z–A</option>
          </select>
          <input type="search" value={workQueueSearch} onChange={(event) => setWorkQueueSearch(event.target.value)} placeholder="Case or item" aria-label="Search queue" />
          <FilterSummary>{workQueueFilteredCount} of {totalForFacet} item{totalForFacet === 1 ? '' : 's'} match</FilterSummary>
          {(workQueueFilter === 'all' || workQueueFilter === 'service') && (
            <div className="ui-filterbar-row2">
              {serviceConditionChips.map((chip) => (
                <FilterChip key={chip.key} active={serviceConditionFilter === chip.key} onClick={() => setServiceConditionFilter(chip.key)}>
                  {chip.label}
                </FilterChip>
              ))}
            </div>
          )}
        </FilterBar>

        {workQueueFilter === 'deadlines' ? renderDeadlinesFacetTable()
          : workQueueFilter === 'checklist' ? renderTasksFacetTable()
          : workQueueFilter === 'service' ? renderServiceFacetTable()
          : renderUnifiedTable()}
      </main>
    )
  }

  function renderCaseWorkspace() {
    const caseId = selectedCaseId ?? caseDraft.id
    const isNewCase = !caseId
    const commandDeadlines = workspace ? workspace.deadlines.filter((item) => !isDeadlineDone(item)).sort((a, b) => (a.dueDate || '9999-12-31').localeCompare(b.dueDate || '9999-12-31')).slice(0, 3) : []
    const commandChecklist = workspace ? sortChecklistForDisplay(workspace.checklistItems.filter((item) => !isChecklistDone(item))).slice(0, 3) : []
    const commandDiscovery = workspace ? workspace.discoveryItems.filter((item) => item.status.includes('Follow-Up') || item.status.includes('Waiting')).slice(0, 3) : []
    const coreRecordFields = [
      { label: 'Filing Date', value: displayDate(selectedCase.filingDate), important: Boolean(selectedCase.filingDate), always: false },
      { label: 'Date of Taking', value: displayDate(selectedCase.dateOfTaking), important: Boolean(selectedCase.dateOfTaking), always: false },
      { label: 'Trial / Hearing Date', value: displayDate(selectedCase.trialDate), important: true, always: false },
      { label: 'Closed Date', value: displayDate(selectedCase.closedDate), important: Boolean(selectedCase.closedDate), always: false },
      { label: 'Project Name', value: selectedCase.projectName || '', important: Boolean(selectedCase.projectName), always: false },
    ]
    const peopleRecordFields = [
      { label: 'Assigned Attorney', value: selectedCase.assignedAttorney || '', important: Boolean(selectedCase.assignedAttorney) },
      { label: 'Opposing Counsel', value: selectedCase.opposingCounsel || '', important: Boolean(selectedCase.opposingCounsel) },
      { label: 'Owner', value: selectedCase.owner || '', important: Boolean(selectedCase.owner) },
      { label: 'Landowner', value: selectedCase.landowner || '', important: Boolean(selectedCase.landowner) },
      { label: 'Appraiser', value: selectedCase.appraiser || '', important: Boolean(selectedCase.appraiser) },
      { label: "Landowner's Appraiser", value: selectedCase.landownerAppraiserName || '', important: Boolean(selectedCase.landownerAppraiserName) },
    ]
    const financialRecordFields = [
      { label: 'Deposit Amount', value: displayCurrency(selectedCase.depositAmount), important: true },
      { label: 'Additional Deposit Amount', value: displayCurrency(selectedCase.additionalDepositAmount), important: Boolean(selectedCase.additionalDepositAmount) },
      { label: 'Additional Deposit Date', value: displayDate(selectedCase.additionalDepositDate), important: Boolean(selectedCase.additionalDepositDate) },
      { label: 'Whole Property (acres)', value: formatRecordValue(selectedCase.wholePropertyAcres), important: Boolean(selectedCase.wholePropertyAcres) },
      { label: 'Acquisition (acres)', value: formatRecordValue(selectedCase.acquisitionAcres), important: Boolean(selectedCase.acquisitionAcres) },
      { label: 'Taxes Owed?', value: selectedCase.taxesOwed || '', important: Boolean(selectedCase.taxesOwed) },
      { label: 'Tax Amount Owed', value: selectedCase.taxOwedAmount == null ? '' : displayCurrency(selectedCase.taxOwedAmount), important: Boolean(selectedCase.taxOwedAmount) },
      { label: 'Funds Withdrawn?', value: selectedCase.fundsWithdrawn || '', important: Boolean(selectedCase.fundsWithdrawn) },
      { label: 'Funds Withdrawn Date', value: displayDate(selectedCase.fundsWithdrawnDate), important: Boolean(selectedCase.fundsWithdrawnDate) },
      { label: 'Discovery Completed?', value: selectedCase.discoveryCompleted || '', important: Boolean(selectedCase.discoveryCompleted) },
      { label: 'Updated Appraisal?', value: selectedCase.updatedAppraisal || '', important: Boolean(selectedCase.updatedAppraisal) },
    ]
    // Definition-list display normalization: legacy formatters emit 'Not set' / '—' for missing
    // values; the kv list renders them all as a muted em dash per the design language.
    const recordValueDisplay = (value: string) => (!value || value === 'Not set' || value === '—' ? '—' : value)
    const recordValueEmpty = (value: string) => recordValueDisplay(value) === '—'

    return (
      <main className="page">
        <div className="button-row compact-actions">
          <Btn variant="ghost" size="sm" onClick={goToCaseList}>◂ Cases</Btn>
        </div>

        <section className="workspace-header-compact">
          <div className="workspace-header-top">
            <div>
              <h2>{selectedCase.caseName || 'New Case'}</h2>
              <p className="subtle-text ui-data">
                {selectedCase.caseNumber || 'No case number yet'} &middot; Job {selectedCase.jobNumber || 'not set'} &middot; Tract {selectedCase.tract || 'not set'} &middot; {selectedCase.county || 'County not set'}
              </p>
            </div>
            <div className="workspace-header-pills">
              <StatusChip tone={caseStatusTone(selectedCase.caseStatus)}>{selectedCase.caseStatus || 'Pipeline'}</StatusChip>
              {/* Active/Closed is an overall case state, not a peer of Stage/Track - only shown
                  here (quietly) when Closed; the actual toggle lives on the Status tab. */}
              {selectedCase.status === 'Closed' && <StatusChip tone="neutral">Closed</StatusChip>}
              {selectedCase.statusMappingReview && <StatusChip tone="warn">Status mapping review</StatusChip>}
              {!isNewCase && <Btn onClick={startEditCase}>Edit Case</Btn>}
              {!isNewCase && (
                <div className="case-menu" ref={caseMenuRef}>
                  <Btn variant="ghost" size="sm" className="ui-btn-icon" aria-label="Case menu" aria-haspopup="true" aria-expanded={caseMenuOpen} onClick={() => setCaseMenuOpen((open) => !open)}>⋯</Btn>
                  {caseMenuOpen && (
                    <div className="case-menu-popover" role="menu">
                      <button type="button" role="menuitem" className="case-menu-item case-menu-item-danger" onClick={() => { setCaseMenuOpen(false); void deleteSelectedCase() }}>Delete case…</button>
                    </div>
                  )}
                </div>
              )}
            </div>
          </div>
          {workspace && workspace.caseIssueTags.length > 0 && (
            <div className="chip-row top-gap-small">
              {workspace.caseIssueTags.map((tag) => (
                <span key={tag.id} className="chip" title={tag.category ?? undefined}>{tag.tagName}</span>
              ))}
            </div>
          )}
          {selectedCase.status === 'Triage' && (
            <div className="inline-message warn top-gap-small">
              This imported case is awaiting triage — no deadlines or alerts are generated until intake is completed.
              <button className="primary" style={{ marginLeft: '0.75rem' }} onClick={() => setTriageWizardOpen(true)}>Start Triage</button>
            </div>
          )}
          <div className="ui-tiles keydates-row">
            {selectedCase.filingDate && <MetricTile label="Filing date" value={displayDate(selectedCase.filingDate)} />}
            {selectedCase.dateOfTaking && <MetricTile label="Date of taking" value={displayDate(selectedCase.dateOfTaking)} />}
            {selectedCase.trialDate && <MetricTile label="Trial / hearing" value={displayDate(selectedCase.trialDate)} tone="warn" />}
            {((selectedCase.caseStatus || 'Pipeline') !== 'Pipeline' || selectedCase.depositAmount != null) && <MetricTile label="Deposit" value={displayCurrency(selectedCase.depositAmount)} />}
            {selectedCase.serviceRequired && !selectedCase.servicePerfected && selectedCase.serviceDeadline120 && <MetricTile label="Service deadline" value={displayDate(selectedCase.serviceDeadline120)} tone="danger" />}
          </div>
        </section>

        <div className="segmented-tabs">
          {caseTabs.map((tab) => (
            <button key={tab.key} className={tab.key === caseTab ? 'segment active' : 'segment'} onClick={() => setCaseTab(tab.key)}>
              {tab.label}
            </button>
          ))}
        </div>

        {caseTab === 'overview' && (
          <div className="workspace-sections">
            <section className="quick-actions-toolbar">
              <div>
                <p className="eyebrow dark">Status</p>
                <p className="helper-text">Start here to see what is driving the file right now and jump straight into the next task.</p>
              </div>
              <div className="button-row compact-actions">
                <button className="primary" onClick={() => startDeadlineModal()}>Add Deadline</button>
                <button onClick={() => startChecklistModal()}>Add Task</button>
                <button onClick={() => startDiscoveryModal()}>Add Discovery Item</button>
                {!isNewCase && <button onClick={() => { startNewCaseNote(); setCaseTab('notes') }}>Add Note</button>}
                {!isNewCase && (
                  <button onClick={() => void changeStatus(selectedCase.status === 'Closed' ? 'Active' : 'Closed')}>
                    {selectedCase.status === 'Closed' ? 'Reopen Case' : 'Close Case'}
                  </button>
                )}
              </div>
            </section>

            {overviewWarnings.length > 0 && (
              <div className="warning-banner-strip" role="status">
                <span className="warning-banner-icon" aria-hidden="true">⚠</span>
                <div className="warning-banner-text">
                  {overviewWarnings.map((warning) => <span key={warning}>{warning}</span>)}
                </div>
              </div>
            )}

            {selectedCase.deferredUntil && (
              <div className="warning-banner-strip deferment-alert" role="status">
                <span className="warning-banner-icon" aria-hidden="true">⏸</span>
                <div className="warning-banner-text">
                  <strong>Case deferred until {displayDate(selectedCase.deferredUntil)}</strong>
                  {selectedCase.deferredReason && <span>{selectedCase.deferredReason}</span>}
                  {defermentDateEditOpen && (
                    <div className="button-row compact-actions top-gap-small">
                      <input type="date" value={defermentDateDraft || selectedCase.deferredUntil} onChange={(event) => setDefermentDateDraft(event.target.value)} />
                      <button className="primary" onClick={() => void saveDefermentDate()} disabled={!defermentDateDraft}>Save date</button>
                      <button onClick={() => setDefermentDateEditOpen(false)}>Cancel</button>
                    </div>
                  )}
                  {!defermentDateEditOpen && (
                    <div className="button-row compact-actions top-gap-small">
                      <button onClick={() => { setDefermentDateDraft(selectedCase.deferredUntil ?? ''); setDefermentDateEditOpen(true) }}>Change deferment date</button>
                      <button onClick={() => void clearDeferment()}>Clear deferment</button>
                    </div>
                  )}
                </div>
              </div>
            )}

            <div className="command-summary-strip">
              <button className="summary-pill clickable" onClick={() => setCaseTab('work')}><span>Open deadlines</span><strong>{String(workspace?.deadlines.filter((item) => !isDeadlineDone(item)).length ?? 0)}</strong></button>
              <button className="summary-pill clickable" onClick={() => setCaseTab('work')}><span>Open tasks</span><strong>{String(openChecklistCount)}</strong></button>
              <button className="summary-pill clickable" onClick={() => setCaseTab('discovery')}><span>{discoveryPosture?.isComplete ? 'Discovery Complete' : 'Discovery follow-up'}</span><strong>{discoveryPosture?.isComplete ? '✓' : String(openDiscoveryCount)}</strong></button>
              <button className="summary-pill clickable" onClick={() => setCaseTab('work')}><span>Next hearing</span><strong>{displayDate(selectedCase.trialDate)}</strong></button>
            </div>

            <div className="workspace-panel-grid two-col">
              <Panel title="Next Deadlines" headerAction={<button className="link-button" onClick={() => setCaseTab('work')}>View All</button>}>
                {commandDeadlines.length === 0 ? <p>No open deadlines right now.</p> : (
                  <div className="command-list">
                    {commandDeadlines.map((item) => (
                      <div key={item.id} className="command-list-row-compact clickable-row" onClick={() => startDeadlineModal(item)}>
                        <input
                          type="checkbox"
                          onClick={(event) => event.stopPropagation()}
                          onChange={() => void persistDeadline({ ...item, status: 'Done' }, 'Deadline marked done.', false)}
                          aria-label={`Mark "${item.title}" done`}
                        />
                        <div>
                          <strong>{item.title}</strong>
                          <div className="flag-text muted">{displayDate(item.dueDate)} | {item.severity}</div>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </Panel>

              <Panel title="Next Tasks" headerAction={<button className="link-button" onClick={() => setCaseTab('work')}>View All</button>}>
                {commandChecklist.length === 0 ? <p>No open tasks right now.</p> : (
                  <div className="command-list">
                    {commandChecklist.map((item) => (
                      <div key={item.id} className="command-list-row-compact clickable-row" onClick={() => startChecklistModal(item)}>
                        <input
                          type="checkbox"
                          onClick={(event) => event.stopPropagation()}
                          onChange={() => void persistChecklist({ ...item, status: 'Done' }, 'Task marked done.', false)}
                          aria-label={`Mark "${item.task}" done`}
                        />
                        <div>
                          <strong>{item.task}</strong>
                          <div className="flag-text muted">{item.phase || 'General'} | {displayDate(item.dueDate)}</div>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </Panel>

              <Panel title="Recent Activity">
                {activityLog.length === 0 ? <p>No activity recorded yet.</p> : (
                  <div className="command-list">
                    {activityLog.slice(0, 10).map((entry) => (
                      <div key={entry.id} className="command-list-row-compact">
                        {editingActivityId === entry.id ? (
                          <form
                            className="inline-quick-form"
                            onSubmit={(event) => { event.preventDefault(); void saveActivityEdit() }}
                          >
                            <label>
                              What happened
                              <select value={activityEditDraft.activityType} onChange={(event) => setActivityEditDraft({ ...activityEditDraft, activityType: event.target.value })}>
                                {!ACTIVITY_TYPE_GROUPS.some((g) => g.types.includes(activityEditDraft.activityType)) && (
                                  <option value={activityEditDraft.activityType}>{activityTypeLabel(activityEditDraft.activityType)}</option>
                                )}
                                {ACTIVITY_TYPE_GROUPS.map((group) => (
                                  <optgroup key={group.label} label={group.label}>
                                    {group.types.map((t) => <option key={t} value={t}>{activityTypeLabel(t)}</option>)}
                                  </optgroup>
                                ))}
                              </select>
                            </label>
                            <label>
                              Occurred on
                              <input type="date" value={activityEditDraft.occurredAt} onChange={(event) => setActivityEditDraft({ ...activityEditDraft, occurredAt: event.target.value })} required />
                            </label>
                            <label>
                              Notes
                              <input value={activityEditDraft.notes} onChange={(event) => setActivityEditDraft({ ...activityEditDraft, notes: event.target.value })} />
                            </label>
                            <label>
                              Reason for change
                              <input value={activityEditDraft.reason} onChange={(event) => setActivityEditDraft({ ...activityEditDraft, reason: event.target.value })} placeholder="Why this entry is being corrected" required />
                            </label>
                            <button className="primary" type="submit">Save</button>
                            <button type="button" onClick={() => setEditingActivityId(null)}>Cancel</button>
                          </form>
                        ) : (
                          <div className={entry.activityType === 'CaseNoteAdded' ? 'activity-audit-row activity-navigable' : 'activity-audit-row'} onClick={entry.activityType === 'CaseNoteAdded' ? () => setCaseTab('notes') : undefined} title={entry.activityType === 'CaseNoteAdded' ? 'Open related case note' : 'System audit entry; not editable'}>
                            <strong>
                              {activityTypeLabel(entry.activityType)}
                              {entry.history.length > 0 && <span className="flag-text muted"> (edited)</span>}
                            </strong>
                            <div className="flag-text muted">{displayDateTime(entry.occurredAt)}{entry.notes ? ` | ${entry.notes}` : ''}</div>
                            {entry.history.length > 0 && (
                              <EditHistoryList
                                rows={entry.history.map((h) => ({
                                  id: h.id,
                                  previous: `${activityTypeLabel(h.previousType ?? '')} ${h.previousOccurredAt ? displayDate(h.previousOccurredAt) : ''}${h.previousNotes ? ` | ${h.previousNotes}` : ''}`.trim(),
                                  next: `${activityTypeLabel(h.newType ?? '')} ${h.newOccurredAt ? displayDate(h.newOccurredAt) : ''}${h.newNotes ? ` | ${h.newNotes}` : ''}`.trim(),
                                  reason: h.reason,
                                  createdAt: h.createdAt,
                                }))}
                              />
                            )}
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                )}
              </Panel>

              <Panel title="Discovery Follow-Up" headerAction={<button className="link-button" onClick={() => setCaseTab('discovery')}>View All</button>}>
                {commandDiscovery.length === 0 ? <p>No active discovery follow-up items.</p> : (
                  <div className="command-list">
                    {commandDiscovery.map((item) => (
                      <div key={item.id} className="command-list-row-compact clickable-row" onClick={() => startDiscoveryModal(item)}>
                        <input
                          type="checkbox"
                          onClick={(event) => event.stopPropagation()}
                          onChange={() => void persistDiscovery({ ...item, status: 'Complete' }, 'Discovery item marked complete.', false)}
                          aria-label={`Mark "${item.requestTitle || `${item.direction} ${item.discoveryType}`}" complete`}
                        />
                        <div>
                          <strong>{item.requestTitle || `${item.direction} ${item.discoveryType}`}</strong>
                          <div className="flag-text muted">{item.status} | follow-up {displayDate(item.followUpDate || item.dueDate)}</div>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </Panel>
            </div>

            <CollapsiblePanel title="Case record" defaultOpen={false}>
              <div className="button-row compact-actions">
                <Btn onClick={startEditCase}>Edit case record</Btn>
                <Btn onClick={() => setShowAllCaseRecordFields((current) => !current)}>{showAllCaseRecordFields ? 'Hide empty fields' : 'Show all fields'}</Btn>
              </div>
              <div className="record-section-stack top-gap">
                <div className="record-section">
                  <div className="record-section-header">
                    <h4>Core Case</h4>
                    <p>Identity and calendar fields not already shown in the case header.</p>
                  </div>
                  <div className="kv record-kv">
                    {coreRecordFields.filter((field) => showAllCaseRecordFields || field.always || field.important || shouldShowRecordValue(field.value)).map((field) => (
                      <Fragment key={field.label}>
                        <span className="record-kv-label">{field.label}</span>
                        <span className={`v${recordValueEmpty(field.value) ? ' record-kv-empty' : ''}`}>{recordValueDisplay(field.value)}</span>
                      </Fragment>
                    ))}
                  </div>
                </div>

                <div className="record-section">
                  <div className="record-section-header">
                    <h4>People</h4>
                    <p>Attorney, opposing counsel, and appraisal contacts.</p>
                  </div>
                  <div className="kv record-kv">
                    {peopleRecordFields.filter((field) => showAllCaseRecordFields || field.important || shouldShowRecordValue(field.value)).map((field) => (
                      <Fragment key={field.label}>
                        <span className="record-kv-label">{field.label}</span>
                        <span className={`v${recordValueEmpty(field.value) ? ' record-kv-empty' : ''}`}>{recordValueDisplay(field.value)}</span>
                      </Fragment>
                    ))}
                  </div>
                </div>

                <div className="record-section">
                  <div className="record-section-header">
                    <h4>Financial / Property</h4>
                    <p>Deposit, acreage, tax, and valuation-related reference data.</p>
                  </div>
                  <div className="kv record-kv">
                    {financialRecordFields.filter((field) => showAllCaseRecordFields || field.important || shouldShowRecordValue(field.value)).map((field) => (
                      <Fragment key={field.label}>
                        <span className="record-kv-label">{field.label}</span>
                        <span className={`v${recordValueEmpty(field.value) ? ' record-kv-empty' : ''}`}>{recordValueDisplay(field.value)}</span>
                      </Fragment>
                    ))}
                  </div>
                  {(showAllCaseRecordFields || shouldShowRecordValue(selectedCase.valuationNotes)) && (
                    <div className="record-kv-notes">
                      <span className="record-kv-label">Valuation Notes</span>
                      <p className="preformatted-note">{selectedCase.valuationNotes || 'No valuation notes yet.'}</p>
                    </div>
                  )}
                  {(showAllCaseRecordFields || shouldShowRecordValue(selectedCase.settlementNotes)) && (
                    <div className="record-kv-notes">
                      <span className="record-kv-label">Settlement Notes</span>
                      <p className="preformatted-note">{selectedCase.settlementNotes || 'No settlement notes yet.'}</p>
                    </div>
                  )}
                </div>
              </div>
            </CollapsiblePanel>
          </div>
        )}

        {caseTab === 'servicePublication' && (
          <div className="workspace-sections">
            <Panel title="Service &amp; Publication">
              <p className="helper-text">Primary factual record for service perfection and publication.</p>
              <div className="button-row compact-actions top-gap-small">
                <span>Service Perfected</span>
                <span className={`pill pill-${selectedCase.servicePerfected ? 'success' : 'neutral'}`}>{selectedCase.servicePerfected ? 'Perfected' : 'Not Perfected'}</span>
                {!servicePerfectedConfirming ? (
                  <button onClick={() => setServicePerfectedConfirming(true)}>{selectedCase.servicePerfected ? 'Mark Not Perfected…' : 'Mark Perfected…'}</button>
                ) : (
                  <span className="button-row compact-actions">
                    <span className="helper-text">{selectedCase.servicePerfected ? 'Confirm reverting this case to not perfected?' : 'Confirm service has been perfected for this case?'}</span>
                    <button className="primary" onClick={() => { void toggleServicePerfected(!selectedCase.servicePerfected); setServicePerfectedConfirming(false) }}>Confirm</button>
                    <button onClick={() => setServicePerfectedConfirming(false)}>Cancel</button>
                  </span>
                )}
              </div>
              <form className="form-grid top-gap-small" onSubmit={savePublication}>
                <label><span>First Publication Date</span><input type="date" value={publicationDraft.firstPublicationDate || ''} onChange={(event) => setPublicationDraft({ ...publicationDraft, firstPublicationDate: event.target.value })} /></label>
                <label><span>Second Publication Date</span><input type="date" min={publicationDraft.firstPublicationDate || undefined} value={publicationDraft.secondPublicationDate || ''} onChange={(event) => setPublicationDraft({ ...publicationDraft, secondPublicationDate: event.target.value })} /></label>
                <label><span>Newspaper / Publication Name</span><input value={publicationDraft.publicationName || ''} onChange={(event) => setPublicationDraft({ ...publicationDraft, publicationName: event.target.value, overrideMissingPublicationName: false })} /></label>
                {(publicationDraft.firstPublicationDate || publicationDraft.secondPublicationDate) && !publicationDraft.publicationName && <label className="toggle-inline full-span"><span>Override missing publication name warning</span><input type="checkbox" checked={Boolean(publicationDraft.overrideMissingPublicationName)} onChange={(event) => setPublicationDraft({ ...publicationDraft, overrideMissingPublicationName: event.target.checked })} /></label>}
                <div className="button-row compact-actions full-span"><button className="primary" type="submit">Save Service &amp; Publication</button></div>
                <p className="helper-text full-span">Last updated {displayDateTime(workspace?.publication?.lastUpdatedAt)} by {workspace?.publication?.lastUpdatedBy || 'Local development user'}.</p>
              </form>
            </Panel>

            <Panel title="Service Log" headerAction={<button className="primary" onClick={startNewServiceLogEntry}>Add Service Entry</button>}>
              <p className="helper-text">Track each defendant separately - who's served, who isn't, method, dates, and attempts.</p>
              {serviceLogFormOpen && (
                <form className="form-grid top-gap-small" onSubmit={(event) => { event.preventDefault(); void saveServiceLogEntry() }}>
                  <label><span>Party Name</span><input value={serviceLogDraft.partyName} onChange={(event) => setServiceLogDraft({ ...serviceLogDraft, partyName: event.target.value })} placeholder="Defendant name" required /></label>
                  <label><span>Status</span><select value={serviceLogDraft.status} onChange={(event) => setServiceLogDraft({ ...serviceLogDraft, status: event.target.value })}>{serviceLogStatuses.map((status) => <option key={status}>{status}</option>)}</select></label>
                  <label><span>Method</span><input value={serviceLogDraft.method || ''} onChange={(event) => setServiceLogDraft({ ...serviceLogDraft, method: event.target.value })} placeholder="e.g. Certified mail, Sheriff, Publication" /></label>
                  <label><span>Date</span><input type="date" value={serviceLogDraft.eventDate || ''} onChange={(event) => setServiceLogDraft({ ...serviceLogDraft, eventDate: event.target.value })} /></label>
                  <label className="full-span"><span>Notes</span><textarea value={serviceLogDraft.notes || ''} onChange={(event) => setServiceLogDraft({ ...serviceLogDraft, notes: event.target.value })} placeholder="Attempt details, wrangling notes" /></label>
                  <div className="button-row compact-actions full-span">
                    <button className="primary" type="submit">{serviceLogDraft.id === 0 ? 'Save Entry' : 'Update Entry'}</button>
                    <button type="button" onClick={() => setServiceLogFormOpen(false)}>Cancel</button>
                  </div>
                </form>
              )}
              {serviceLogEntries.length === 0 ? <p className="top-gap-small">No parties logged yet.</p> : (
                <div className="table-wrap top-gap-small">
                  <table className="compact-table">
                    <thead><tr><th>Party</th><th>Status</th><th>Method</th><th>Date</th><th>Notes</th><th>Last Updated</th><th>Actions</th></tr></thead>
                    <tbody>
                      {serviceLogEntries.map((entry) => (
                        <tr key={entry.id}>
                          <td>{entry.partyName}</td>
                          <td>
                            <select
                              className={`inline-edit-select pill-select pill-${entry.status === 'Served' ? 'success' : entry.status === 'Refused' ? 'danger' : entry.status === 'Attempted' ? 'warn' : 'neutral'}`}
                              value={entry.status}
                              aria-label={`Status for ${entry.partyName}`}
                              onChange={(event) => void updateServiceLogStatus(entry, event.target.value)}
                            >
                              {serviceLogStatuses.map((status) => <option key={status} value={status}>{status}</option>)}
                            </select>
                          </td>
                          <td>{entry.method || 'Not set'}</td>
                          <td>{displayDate(entry.eventDate)}</td>
                          <td>{entry.notes || '—'}</td>
                          <td>{entry.updatedAt ? displayDateTime(entry.updatedAt) : 'Not set'}</td>
                          <td>
                            <div className="button-row compact-actions row-actions">
                              <button onClick={() => startEditServiceLogEntry(entry)}>Edit</button>
                              <button onClick={() => void deleteServiceLogEntry(entry)}>Delete</button>
                            </div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Panel>

            <Panel title="Publication" headerAction={<button className="primary" onClick={startNewPublicationEntry}>Add Publication Entry</button>}>
              <p className="helper-text">Multiple publication attempts for unknown/unlocatable owners - newspaper, dates, and proof filed.</p>
              {publicationEntryFormOpen && (
                <form className="form-grid top-gap-small" onSubmit={(event) => { event.preventDefault(); void savePublicationEntry() }}>
                  <label><span>Publication Number</span><input value={publicationEntryDraft.publicationNumber} onChange={(event) => setPublicationEntryDraft({ ...publicationEntryDraft, publicationNumber: event.target.value })} placeholder="e.g. 1st, 2nd" /></label>
                  <label><span>Newspaper</span><input value={publicationEntryDraft.newspaper || ''} onChange={(event) => setPublicationEntryDraft({ ...publicationEntryDraft, newspaper: event.target.value })} /></label>
                  <label><span>Publication Date</span><input type="date" value={publicationEntryDraft.publicationDate || ''} onChange={(event) => setPublicationEntryDraft({ ...publicationEntryDraft, publicationDate: event.target.value })} /></label>
                  <label className="toggle-inline"><span>Proof Filed</span><input type="checkbox" checked={publicationEntryDraft.proofFiled} onChange={(event) => setPublicationEntryDraft({ ...publicationEntryDraft, proofFiled: event.target.checked })} /></label>
                  {publicationEntryDraft.proofFiled && <label><span>Proof Filed Date</span><input type="date" value={publicationEntryDraft.proofFiledDate || ''} onChange={(event) => setPublicationEntryDraft({ ...publicationEntryDraft, proofFiledDate: event.target.value })} /></label>}
                  <label className="full-span"><span>Notes</span><textarea value={publicationEntryDraft.notes || ''} onChange={(event) => setPublicationEntryDraft({ ...publicationEntryDraft, notes: event.target.value })} /></label>
                  <div className="button-row compact-actions full-span">
                    <button className="primary" type="submit">{publicationEntryDraft.id === 0 ? 'Save Entry' : 'Update Entry'}</button>
                    <button type="button" onClick={() => setPublicationEntryFormOpen(false)}>Cancel</button>
                  </div>
                </form>
              )}
              {publicationEntries.length === 0 ? <p className="top-gap-small">No publication entries yet.</p> : (
                <div className="table-wrap top-gap-small">
                  <table className="compact-table">
                    <thead><tr><th>#</th><th>Newspaper</th><th>Date</th><th>Proof Filed</th><th>Actions</th></tr></thead>
                    <tbody>
                      {publicationEntries.map((entry) => (
                        <tr key={entry.id}>
                          <td>{entry.publicationNumber || 'Not set'}</td>
                          <td>{entry.newspaper || 'Not set'}</td>
                          <td>{displayDate(entry.publicationDate)}</td>
                          <td>{entry.proofFiled ? displayDate(entry.proofFiledDate) : 'No'}</td>
                          <td>
                            <div className="button-row compact-actions row-actions">
                              <button onClick={() => startEditPublicationEntry(entry)}>Edit</button>
                              <button onClick={() => void deletePublicationEntry(entry)}>Delete</button>
                            </div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Panel>
          </div>
        )}

        {caseTab === 'work' && renderWorkTab()}

        {caseTab === 'discovery' && (
          <div className="workspace-sections">
            <Panel title="Discovery Strategy">
              {!selectedCase ? <p>Save the case first to set a discovery strategy.</p> : !discoveryPosture ? <p>Loading...</p> : !discoveryStrategyEditing ? (
                <>
                  <div className="button-row split-row">
                    <div>
                      <strong>{discoveryPosture.strategy}</strong>
                      <div className="flag-text muted">
                        {discoveryPosture.nextReviewDate ? `Next review ${displayDate(discoveryPosture.nextReviewDate)}` : 'No next review date'}
                        {discoveryPosture.nextDecision ? ` | ${discoveryPosture.nextDecision}` : ''}
                      </div>
                    </div>
                    <div className="button-row compact-actions row-actions">
                      {discoveryPosture.isComplete && <span className="pill pill-success">Complete</span>}
                      <button onClick={() => setDiscoveryStrategyEditing(true)}>Edit</button>
                    </div>
                  </div>
                  <div className="discovery-overview-row top-gap-small">
                    <label className="discovery-overview-strategy"><span>Quick-change strategy</span><select value={discoveryPosture.strategy} onChange={(event) => { const next = { ...discoveryPosture, strategy: event.target.value }; setDiscoveryPosture(next); void api<DiscoveryPosture>(`/api/cases/${next.caseId}/discovery-posture`, { method: 'POST', body: JSON.stringify(next) }).then(setDiscoveryPosture).catch((error) => setErrorMessage(error instanceof Error ? error.message : 'Unable to update discovery strategy.')) }}>{DISCOVERY_STRATEGIES.map((strategy) => <option key={strategy}>{strategy}</option>)}</select></label>
                    <label className="toggle-inline discovery-complete-toggle"><span>Discovery Complete</span><input type="checkbox" checked={discoveryPosture.isComplete} onChange={(event) => { const next = { ...discoveryPosture, isComplete: event.target.checked }; setDiscoveryPosture(next); void api<DiscoveryPosture>(`/api/cases/${next.caseId}/discovery-posture`, { method: 'POST', body: JSON.stringify(next) }).then(setDiscoveryPosture).catch((error) => setErrorMessage(error instanceof Error ? error.message : 'Unable to update discovery status.')) }} /></label>
                  </div>
                  <p className="helper-text top-gap-small">{discoveryPosture.isComplete ? `Discovery Complete${discoveryPosture.completionChangedAt ? ` · ${displayDateTime(discoveryPosture.completionChangedAt)}` : ''}${discoveryPosture.completionChangedBy ? ` by ${discoveryPosture.completionChangedBy}` : ''}` : 'Discovery remains open. Individual requests and deadlines are unchanged.'}</p>
                </>
              ) : (
                <>
                  <div className="form-grid">
                    <label>
                      <span>Strategy</span>
                      <select value={discoveryPosture.strategy} onChange={(event) => setDiscoveryPosture({ ...discoveryPosture, strategy: event.target.value })}>
                        {DISCOVERY_STRATEGIES.map((s) => <option key={s} value={s}>{s}</option>)}
                      </select>
                    </label>
                    <label className="full-span">
                      <span>Strategy Reason</span>
                      <textarea value={discoveryPosture.strategyReason || ''} onChange={(event) => setDiscoveryPosture({ ...discoveryPosture, strategyReason: event.target.value })} placeholder="Why this approach" rows={2} />
                    </label>
                    <label>
                      <span>Strategy Selected Date</span>
                      <input type="date" value={discoveryPosture.strategySelectedDate || ''} onChange={(event) => setDiscoveryPosture({ ...discoveryPosture, strategySelectedDate: event.target.value })} />
                    </label>
                    <label>
                      <span>Discovery Served Date</span>
                      <input type="date" value={discoveryPosture.discoveryServedDate || ''} onChange={(event) => setDiscoveryPosture({ ...discoveryPosture, discoveryServedDate: event.target.value })} />
                    </label>
                    <label>
                      <span>Responses Due Date</span>
                      <input type="date" value={discoveryPosture.responsesDueDate || ''} onChange={(event) => setDiscoveryPosture({ ...discoveryPosture, responsesDueDate: event.target.value })} />
                    </label>
                    <label>
                      <span>Responses Received Date</span>
                      <input type="date" value={discoveryPosture.responsesReceivedDate || ''} onChange={(event) => setDiscoveryPosture({ ...discoveryPosture, responsesReceivedDate: event.target.value })} />
                    </label>
                    <label>
                      <span>Responses Reviewed Date</span>
                      <input type="date" value={discoveryPosture.responsesReviewedDate || ''} onChange={(event) => setDiscoveryPosture({ ...discoveryPosture, responsesReviewedDate: event.target.value })} />
                    </label>
                    <label>
                      <span>Discovery Cutoff Date</span>
                      <input type="date" value={discoveryPosture.discoveryCutoffDate || ''} onChange={(event) => setDiscoveryPosture({ ...discoveryPosture, discoveryCutoffDate: event.target.value })} />
                    </label>
                    <label>
                      <span>Planned Depositions</span>
                      <input value={discoveryPosture.plannedDepositions || ''} onChange={(event) => setDiscoveryPosture({ ...discoveryPosture, plannedDepositions: event.target.value })} placeholder="Who, and when" />
                    </label>
                    <label>
                      <span>Deficiency Status</span>
                      <input value={discoveryPosture.deficiencyStatus || ''} onChange={(event) => setDiscoveryPosture({ ...discoveryPosture, deficiencyStatus: event.target.value })} placeholder="e.g. Deficiency letter sent 5/1" />
                    </label>
                    <label>
                      <span>Next Decision</span>
                      <input value={discoveryPosture.nextDecision || ''} onChange={(event) => setDiscoveryPosture({ ...discoveryPosture, nextDecision: event.target.value })} placeholder="What needs deciding next" />
                    </label>
                    <label>
                      <span>Next Review Date</span>
                      <input type="date" value={discoveryPosture.nextReviewDate || ''} onChange={(event) => setDiscoveryPosture({ ...discoveryPosture, nextReviewDate: event.target.value })} />
                    </label>
                    <label className="toggle-inline"><span>Discovery Complete</span><input type="checkbox" checked={discoveryPosture.isComplete} onChange={(event) => setDiscoveryPosture({ ...discoveryPosture, isComplete: event.target.checked })} /></label>
                  </div>
                  <div className="button-row compact-actions top-gap-small">
                    <button className="primary" onClick={() => void saveDiscoveryPosture()} disabled={discoveryPostureSaving}>Save Discovery Strategy</button>
                  </div>
                  {discoveryPosture.updatedAt && <p className="helper-text top-gap-small">Last updated {displayDateTime(discoveryPosture.updatedAt)}.</p>}
                </>
              )}
            </Panel>

            <Panel title="Selected-Case Discovery">
              <div className="button-row compact-actions top-gap-small">
                <button className="primary" onClick={() => startDiscoveryModal()}>Add Discovery Item</button>
              </div>
              <p className="helper-text top-gap-small">Use the status menu on each card for quick follow-up, completion, or reopening without jumping into the full editor.</p>
              <div className="top-gap">
                {workspace ? renderDiscoveryAccordion(workspace.discoveryItems) : <p>Save the case first to manage discovery items.</p>}
              </div>
            </Panel>
          </div>
        )}

        {caseTab === 'notes' && (
          <div className="workspace-panel-grid two-col">
            <Panel title={noteDraft.id === 0 ? 'New Note' : 'Edit Note'}>
              <div className="form-grid">
                <label><span>Title</span><input value={noteDraft.title} onChange={(event) => setNoteDraft({ ...noteDraft, title: event.target.value })} placeholder="Short note title" /></label>
                <label className="full-span"><span>Note</span><textarea value={noteDraft.body} onChange={(event) => setNoteDraft({ ...noteDraft, body: event.target.value })} placeholder="Case notes, updates, strategy points, or follow-up reminders" /></label>
              </div>
              <div className="button-row compact-actions top-gap-small">
                <button className="primary" onClick={() => void saveCaseNote()}>{noteDraft.id === 0 ? 'Save Note' : 'Update Note'}</button>
                <button onClick={startNewCaseNote}>Clear</button>
                <button onClick={() => void exportCaseNotes()}>Export Notes</button>
              </div>
              {noteDraft.updatedAt && <p className="helper-text top-gap-small">Last edited {displayDateTime(noteDraft.updatedAt)}.</p>}
            </Panel>

            <Panel title="Case Notes">
              {workspace && workspace.caseNotes.length > 0 ? (
                <div className="stacked-panels compact-stack">
                  {workspace.caseNotes.map((note) => (
                    <article key={note.id} className="summary-card note-card">
                      <div className="button-row split-row">
                        <strong>{note.title}</strong>
                        <div className="button-row compact-actions row-actions">
                          <button onClick={() => startEditCaseNote(note)}>Edit</button>
                          <button onClick={() => void deleteCaseNote(note)}>Delete</button>
                        </div>
                      </div>
                      <p className="helper-text">Created {displayDateTime(note.createdAt)} | Updated {displayDateTime(note.updatedAt)}</p>
                      <p className="preformatted-note top-gap-small">{note.body}</p>
                    </article>
                  ))}
                </div>
              ) : <p>No case notes yet. Use this tab for timestamped case updates instead of burying notes inside the case editor.</p>}
            </Panel>
          </div>
        )}

        {caseTab === 'documents' && (
          <div className="workspace-sections">
            <div className="document-utility-row">
              <span className="helper-text">Case utilities</span>
              <button className="compact-action-button" onClick={() => void generateDocument('summary')}>Generate Case Summary</button>
              <button className="compact-action-button" onClick={() => void generateDocument('memo')}>Generate Case Review</button>
              <button className="compact-action-button" onClick={() => { setPage('settings'); setSettingsSection('documentPlatformTemplates') }}>Manage Document Templates</button>
              <button className="compact-action-button" onClick={() => setShowMergeTagsModal(true)}>Available Merge Fields</button>
            </div>

            <Panel title="Generate a Document">
              <p className="helper-text">Pick a template, check the sections this case needs (issue-tag sections come pre-checked), fill in any per-document fields, and download the draft.</p>
              <div className="button-row top-gap-small">
                <button className="compact-action-button" onClick={() => void loadDocumentPlatformCaseTemplates()}>Load Templates</button>
                <select value={platformCaseTemplateKey} onChange={(event) => void loadPlatformChecklist(event.target.value)}>
                  <option value="">Choose a document template…</option>
                  {platformTemplates.filter((t) => t.activeVersion).map((t) => (
                    <option key={t.template.templateKey} value={t.template.templateKey}>{t.template.title}</option>
                  ))}
                </select>
              </div>
              {platformTemplates.length === 0 && <p className="helper-text top-gap-small">Click "Load Templates" to see the catalog.</p>}
              {platformChecklist && (
                <div className="top-gap">
                  <h3>{platformChecklist.title}</h3>
                  {platformChecklist.sections.length > 0 && (
                    <div className="top-gap-small">
                      {platformChecklist.sections.map((section) => (
                        <div key={section.sectionKey} className="top-gap-small">
                          <label className="toggle-inline">
                            <span>{section.label}{section.issueTagName ? ` (tag: ${section.issueTagName})` : ''}</span>
                            <input
                              type="checkbox"
                              checked={platformSelectedSections.includes(section.sectionKey)}
                              onChange={() => togglePlatformSection(section.sectionKey)}
                            />
                          </label>
                          {section.description && <p className="helper-text">{section.description}</p>}
                          {section.overlapWarnings.map((warning) => (
                            <p key={warning} className="inline-message warn">{warning}</p>
                          ))}
                        </div>
                      ))}
                    </div>
                  )}
                  {platformChecklist.runtimeInputs.length > 0 && (
                    <div className="top-gap-small">
                      <h4>Fields for this document</h4>
                      <div className="form-grid">
                        {platformChecklist.runtimeInputs.map((input) => (
                          <label key={input.fieldKey} className={input.fieldType === 'textarea' ? 'full-span' : ''}>
                            <span>{input.label}{input.isRequired ? ' *' : ''}</span>
                            {input.fieldType === 'textarea' ? (
                              <textarea
                                value={platformRuntimeInputValues[input.fieldKey] ?? ''}
                                onChange={(event) => setPlatformRuntimeInputValue(input.fieldKey, event.target.value)}
                              />
                            ) : (
                              <input
                                type={input.fieldType === 'date' ? 'date' : input.fieldType === 'number' ? 'number' : 'text'}
                                value={platformRuntimeInputValues[input.fieldKey] ?? ''}
                                onChange={(event) => setPlatformRuntimeInputValue(input.fieldKey, event.target.value)}
                              />
                            )}
                          </label>
                        ))}
                      </div>
                    </div>
                  )}
                  <button className="primary top-gap-small" disabled={platformBusy} onClick={() => void generatePlatformDocument()}>
                    {platformBusy ? 'Generating…' : 'Generate & Download'}
                  </button>
                  {platformGenerationResult && (
                    <div className="top-gap-small">
                      {platformGenerationResult.missingFields.length > 0 && (
                        <p className="inline-message warn">Missing values: {platformGenerationResult.missingFields.join(', ')} - the draft still generated with these flagged inline; fill them in before filing.</p>
                      )}
                      <a className="button-like" href={`/api/document-platform-generations/${platformGenerationResult.generationId}/download`}>Download Generated Document</a>
                    </div>
                  )}
                </div>
              )}
            </Panel>

            <Panel title="Generated Documents">
              {(() => {
                const legacyRows = (workspace?.documentExports ?? []).map((item) => ({
                  key: `legacy-${item.id}`,
                  createdAt: item.createdAt,
                  source: 'Legacy' as const,
                  documentType: item.documentType,
                  title: item.documentTitle,
                  isDraft: item.isDraft ?? false,
                  missingFields: [] as string[],
                  downloadHref: `/api/document-exports/${item.id}/download`,
                }))
                const platformRows = platformGenerationHistory.map((item) => ({
                  key: `platform-${item.id}`,
                  createdAt: item.renderedAt,
                  source: 'Platform' as const,
                  documentType: 'Document Platform',
                  title: item.templateTitle,
                  isDraft: item.isDraft,
                  missingFields: item.missingFields,
                  downloadHref: `/api/document-platform-generations/${item.id}/download`,
                }))
                const rows = [...legacyRows, ...platformRows].sort((a, b) => b.createdAt.localeCompare(a.createdAt))
                return rows.length > 0 ? (
                  <div className="table-wrap top-gap">
                    <table>
                      <thead>
                        <tr>
                          <th>Created</th>
                          <th>Source</th>
                          <th>Document Type</th>
                          <th>Title</th>
                          <th>Status</th>
                          <th>Actions</th>
                        </tr>
                      </thead>
                      <tbody>
                        {rows.map((row) => (
                          <tr key={row.key}>
                            <td>{displayDateTime(row.createdAt)}</td>
                            <td><span className={`pill pill-${row.source === 'Platform' ? 'success' : 'neutral'}`}>{row.source}</span></td>
                            <td>{row.documentType}</td>
                            <td>{row.title}</td>
                            <td>
                              <span className={`pill pill-${row.isDraft ? 'warn' : 'success'}`}>{row.isDraft ? 'Draft' : 'Finalized'}</span>
                              {row.missingFields.length > 0 && <span className="pill pill-warn" title={`Missing: ${row.missingFields.join(', ')}`}>Missing fields</span>}
                            </td>
                            <td>
                              <div className="button-row compact-actions row-actions">
                                <a className="button-like" href={row.downloadHref}>Download</a>
                              </div>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                ) : <p className="top-gap">No generated documents yet.</p>
              })()}
            </Panel>
          </div>
        )}

        {caseTab === 'riskAnalysis' && (
          <div className="workspace-sections">
            {riskAnalysisHistory.length > 0 && <Panel title="Saved Risk Analyses">
              <div className="table-wrap compact-table-wrap"><table className="compact-table"><thead><tr><th>Analysis date</th><th>Key scenario</th><th>Just compensation</th><th>Created</th><th>Actions</th></tr></thead><tbody>{riskAnalysisHistory.map((history) => <tr key={history.id}><td>{displayDate(history.analysisDate)}</td><td>{history.keyScenarioLabel || 'No populated scenario'}</td><td>{displayCurrency(history.keyScenarioValue)}</td><td>{displayDate(history.createdAt)}</td><td><div className="button-row compact-actions row-actions"><button onClick={() => void openRiskAnalysisHistory(history.id)}>Open</button><button onClick={() => void compareRiskAnalysisHistory(history)}>Compare</button><button className="danger-button" onClick={() => void deleteRiskAnalysisHistory(history)}>Delete</button></div></td></tr>)}</tbody></table></div>
              <p className="helper-text top-gap-small">Each save retains an immutable snapshot of the analysis as of that date.</p>
              {riskAnalysisComparison && <div className="risk-comparison-card top-gap-small"><div><strong>Saved snapshot</strong><div>{displayDate(riskAnalysisComparison.left.analysisDate)} · {riskAnalysisComparison.left.keyScenarioLabel || 'No key scenario'} · {displayCurrency(riskAnalysisComparison.left.keyScenarioValue)}</div></div><div><strong>Rendered values</strong><div>Total deposited {displayCurrency(riskAnalysisComparison.right.totalDeposited)} · {riskAnalysisComparison.right.rows.filter((row) => !row.isSplit && row.justCompensation != null).length} populated scenarios</div></div><button onClick={() => setRiskAnalysisComparison(null)}>Close comparison</button></div>}
            </Panel>}

            <Panel title="Risk Analysis Ledger" className="panel-featured">
              {!riskAnalysisEditorOpen ? <div className="compact-empty-state risk-analysis-summary-state">
                {riskAnalysisPreview?.id ? <><p>Current risk analysis saved on {riskAnalysisPreview.updatedAt ? displayDateTime(riskAnalysisPreview.updatedAt) : 'recorded date'}.</p><div className="button-row compact-actions"><button className="primary" onClick={() => setRiskAnalysisEditorOpen(true)}>Open Analysis</button>{selectedCaseId && <a className="button-like" href={`/api/cases/${selectedCaseId}/risk-analysis/export`}>Download Excel</a>}</div></> : <><p>No risk analyses added yet.</p><button className="primary" onClick={() => setRiskAnalysisEditorOpen(true)}>Add Risk Analysis</button></>}
              </div> : <>
              <div className="form-grid risk-analysis-inputs">
                <label><span>Analysis Date</span><input type="date" value={riskAnalysisDate} onChange={(event) => setRiskAnalysisDate(event.target.value)} onBlur={() => void recomputeRiskAnalysis(riskAnalysisRows, riskAnalysisNarrative)} /></label>
                <label><span>Interest Rate</span><input type="number" min="0" max="1" step="0.001" value={riskAnalysisInterestRate} onChange={(event) => setRiskAnalysisInterestRate(Number(event.target.value))} onBlur={() => void recomputeRiskAnalysis(riskAnalysisRows, riskAnalysisNarrative)} /></label>
                <label><span>Contingency Fee %</span><input type="number" min="0" max="1" step="0.01" value={riskAnalysisContingencyPercent} onChange={(event) => setRiskAnalysisContingencyPercent(Number(event.target.value))} onBlur={() => void recomputeRiskAnalysis(riskAnalysisRows, riskAnalysisNarrative)} /></label>
              </div>
              <div className="form-grid">
                <label className="full-span">
                  <span>Narrative</span>
                  <textarea
                    value={riskAnalysisNarrative}
                    onChange={(event) => setRiskAnalysisNarrative(event.target.value)}
                    onBlur={() => void recomputeRiskAnalysis(riskAnalysisRows, riskAnalysisNarrative)}
                    placeholder="Notes supporting this analysis"
                  />
                </label>
              </div>
              <div className="button-row compact-actions top-gap-small">
                <button onClick={startNarrativeGeneration}>Generate Narrative</button>
              </div>
              <div className="table-wrap top-gap-small">
                <table className="compact-table risk-analysis-ledger">
                  <thead>
                    <tr>
                      <th>Source</th>
                      <th>Just Compensation</th>
                      <th>Above Deposit</th>
                      <th>Interest (6%)</th>
                      <th>Subtotal</th>
                      <th>LO Fees/Costs</th>
                      <th>ASHC Costs</th>
                      <th>Hourly Fee Risk</th>
                      <th>Contingency Fee (30%)</th>
                      <th>Total Risk (Hourly)</th>
                      <th>Total Risk (Contingency)</th>
                    </tr>
                  </thead>
                  <tbody>
                    {riskAnalysisRows.map((input) => {
                      const row = riskAnalysisPreview?.rows.find((r) => r.rowKey === input.rowKey)
                      const splitRow = input.includeSplit ? riskAnalysisPreview?.rows.find((r) => r.rowKey === `${input.rowKey}Split`) : undefined
                      const dropdownOptions = RISK_ANALYSIS_SLOT_OPTIONS[input.rowKey]
                      return (
                        <Fragment key={input.rowKey}>
                          <tr>
                            <td>
                              {dropdownOptions ? (
                                <select
                                  value={input.label}
                                  onChange={(event) => void patchRiskAnalysisRow(input.rowKey, { label: event.target.value, offerMaker: offerMakerFromLabel(event.target.value) })}
                                  aria-label="Source"
                                >
                                  {dropdownOptions.map((opt) => <option key={opt} value={opt}>{opt}</option>)}
                                </select>
                              ) : (
                                input.label
                              )}
                            </td>
                            <td><NumericField money value={input.justCompensation} onCommit={(value) => void patchRiskAnalysisRow(input.rowKey, { justCompensation: value })} /></td>
                            <td>{displayCurrency(row?.amountAboveInitialDeposit)}</td>
                            <td>{row?.note ? <span className="flag-text muted">{row.note}</span> : displayCurrency(row?.interestOnOverage)}</td>
                            <td>{displayCurrency(row?.subtotal)}</td>
                            <td><NumericField money value={input.landownerFeesCosts} onCommit={(value) => void patchRiskAnalysisRow(input.rowKey, { landownerFeesCosts: value ?? 0 })} /></td>
                            <td><NumericField money value={input.ashcCosts} onCommit={(value) => void patchRiskAnalysisRow(input.rowKey, { ashcCosts: value ?? 0 })} /></td>
                            <td>
                              <select value={input.hourlyFeesRisk} onChange={(event) => void patchRiskAnalysisRow(input.rowKey, { hourlyFeesRisk: Number(event.target.value) })} aria-label="Hourly fee risk">
                                {HOURLY_FEE_RISK_OPTIONS.map((opt) => <option key={opt} value={opt}>{displayCurrency(opt)}</option>)}
                              </select>
                            </td>
                            <td>{displayCurrency(row?.contingencyFee)}</td>
                            <td>{row?.hourlyRiskStatus === 'BelowThreshold' ? <span className="pill pill-success">Below 20% Threshold</span> : displayCurrency(row?.totalRiskHourly)}</td>
                            <td>{displayCurrency(row?.totalRiskContingency)}</td>
                          </tr>
                          {input.includeSplit && (
                            <tr key={`${input.rowKey}Split`} className="muted-row">
                              <td>Split</td>
                              <td>{displayCurrency(splitRow?.justCompensation)}</td>
                              <td>{displayCurrency(splitRow?.amountAboveInitialDeposit)}</td>
                              <td>{splitRow?.note ? <span className="flag-text muted">{splitRow.note}</span> : displayCurrency(splitRow?.interestOnOverage)}</td>
                              <td>{displayCurrency(splitRow?.subtotal)}</td>
                              <td>{displayCurrency(splitRow?.landownerFeesCosts)}</td>
                              <td>{displayCurrency(splitRow?.ashcCosts)}</td>
                              <td>{displayCurrency(splitRow?.hourlyFeesRisk)}</td>
                              <td>{displayCurrency(splitRow?.contingencyFee)}</td>
                              <td>{splitRow?.hourlyRiskStatus === 'BelowThreshold' ? <span className="pill pill-success">Below 20% Threshold</span> : displayCurrency(splitRow?.totalRiskHourly)}</td>
                              <td>{displayCurrency(splitRow?.totalRiskContingency)}</td>
                            </tr>
                          )}
                        </Fragment>
                      )
                    })}
                  </tbody>
                </table>
              </div>
              <div className="button-row compact-actions top-gap-small">
                <button className="primary" onClick={() => void saveRiskAnalysis()} disabled={riskAnalysisSaving}>{riskAnalysisSaving ? 'Saving…' : 'Save'}</button>
                <button onClick={() => void resetRiskAnalysis()}>Reset</button>
                {selectedCaseId && <a className="button-like" href={`/api/cases/${selectedCaseId}/risk-analysis/export`}>Download Excel</a>}
              </div>
              {riskAnalysisPreview && <p className="helper-text top-gap-small">Last saved {riskAnalysisPreview.updatedAt ? displayDateTime(riskAnalysisPreview.updatedAt) : 'never'}.</p>}
              </>}
            </Panel>

            <Panel title="Old Offers">
              <p className="helper-text">Historical offers and counteroffers beyond the current ledger above - matches the "Old Offers" log at the bottom of the office's Risk Analysis spreadsheet.</p>
              {offerLog.length === 0 ? <p className="top-gap-small">No old offers logged yet.</p> : (
                <div className="table-wrap top-gap-small">
                  <table className="compact-table">
                    <thead>
                      <tr><th>Date</th><th>Party</th><th>Amount</th><th>Actions</th></tr>
                    </thead>
                    <tbody>
                      {offerLog.map((entry) => (
                        <tr key={entry.id}>
                          <td>{displayDate(entry.offerDate)}</td>
                          <td>{entry.party}</td>
                          <td>{displayCurrency(entry.amount)}</td>
                          <td>
                            <div className="button-row compact-actions row-actions">
                              <button onClick={() => startEditOfferLogEntry(entry)}>Edit</button>
                              <button onClick={() => void deleteOfferLogEntry(entry)}>Delete</button>
                            </div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
              {!offerLogFormOpen && <div className="button-row compact-actions top-gap"><button className="primary" onClick={startNewOfferLogEntry}>Add Offer</button></div>}
              {offerLogFormOpen && <form className="form-grid top-gap" onSubmit={(event) => { event.preventDefault(); void saveOfferLogEntry() }}>
                <label><span>Date</span><input type="date" value={offerLogDraft.offerDate ?? ''} onChange={(event) => setOfferLogDraft({ ...offerLogDraft, offerDate: event.target.value || null })} /></label>
                <label><span>Party</span><input value={offerLogDraft.party} onChange={(event) => setOfferLogDraft({ ...offerLogDraft, party: event.target.value })} placeholder="Who made this offer" /></label>
                <label><span>Amount</span><NumericField money value={offerLogDraft.amount} onCommit={(value) => setOfferLogDraft({ ...offerLogDraft, amount: value })} /></label>
                <div className="button-row compact-actions full-span">
                  <button className="primary" type="submit">{offerLogDraft.id === 0 ? 'Add Old Offer' : 'Update Old Offer'}</button>
                  <button type="button" onClick={() => { setOfferLogFormOpen(false); setOfferLogDraft(emptyOfferLogEntry(selectedCaseId ?? caseDraft.id)) }}>Cancel</button>
                </div>
              </form>}
            </Panel>

            <CollapsiblePanel title="Case & Deposit Summary" defaultOpen={false}>
              <div className="metric-tile-row">
                <div className="metric-tile"><span>Initial Deposit</span><strong>{displayCurrency(selectedCase.depositAmount)}</strong></div>
                <div className="metric-tile"><span>Additional Deposit</span><strong>{displayCurrency(selectedCase.additionalDepositAmount)}</strong></div>
                <div className="metric-tile"><span>Total Deposited</span><strong>{displayCurrency((selectedCase.depositAmount ?? 0) + (selectedCase.additionalDepositAmount ?? 0))}</strong></div>
                <div className="metric-tile"><span>Days Since Filing</span><strong>{riskAnalysisPreview?.daysSinceFiling ?? '—'}</strong></div>
              </div>
              <div className="metric-tile-row top-gap-small">
                <div className="metric-tile"><span>Filing Date</span><strong>{displayDate(selectedCase.filingDate)}</strong></div>
                <div className="metric-tile"><span>Additional Deposit Date</span><strong>{displayDate(selectedCase.additionalDepositDate)}</strong></div>
                <div className="metric-tile"><span>Whole Property (acres)</span><strong>{selectedCase.wholePropertyAcres ?? '—'}</strong></div>
                <div className="metric-tile"><span>Acquisition (acres)</span><strong>{selectedCase.acquisitionAcres ?? '—'}</strong></div>
              </div>
              <div className="metric-tile-row top-gap-small">
                <div className="metric-tile"><span>Assigned Attorney</span><strong>{selectedCase.assignedAttorney || '—'}</strong></div>
                <div className="metric-tile"><span>Opposing Counsel</span><strong>{selectedCase.opposingCounsel || '—'}</strong></div>
                <div className="metric-tile"><span>Appraiser (ASHC)</span><strong>{selectedCase.appraiser || '—'}</strong></div>
                <div className="metric-tile"><span>Appraiser (Landowner)</span><strong>{selectedCase.landownerAppraiserName || '—'}</strong></div>
              </div>
              <p className="helper-text top-gap-small">Deposit amounts, filing date, and additional deposit date come from Case Record. Interest accrues at 6% per annum on the amount above the deposit, per Ark. Code Ann. § 27-67-316(e).</p>
            </CollapsiblePanel>

            <CollapsiblePanel title="Valuation Comparison" defaultOpen={false}>
              <div className="metric-tile-row">
                <div className="metric-tile"><span>ASHC Value</span><strong>{displayCurrency(ashcValue)}</strong></div>
                <div className="metric-tile"><span>Landowner Value</span><strong>{displayCurrency(landownerValue)}</strong></div>
                <div className="metric-tile"><span>Gap</span><strong>{valuationGap != null ? displayCurrency(valuationGap) : 'Not set'}</strong></div>
                <div className="metric-tile"><span>Gap / Acre</span><strong>{gapPerAcre != null ? displayCurrency(gapPerAcre) : 'Not set'}</strong></div>
              </div>
              <p className="helper-text top-gap-small">Gap is the landowner's position minus ASHC's position. Gap / Acre uses Acquisition Acres from Case Details.</p>
            </CollapsiblePanel>

            <div className="workspace-panel-grid two-col">
              {(['ASHC', 'Landowner'] as ValuationSide[]).map((side) => (
                <CollapsiblePanel key={side} title={`${side} Position`} defaultOpen={false}>
                  {(editingValuationSide === side || valuationDrafts[side].appraiserName || valuationDrafts[side].appraisedValue != null || valuationDrafts[side].methodology || valuationDrafts[side].notes) ? <>
                  <div className="form-grid">
                    <label><span>Appraiser</span><input value={valuationDrafts[side].appraiserName || ''} onChange={(event) => patchValuationDraft(side, { appraiserName: event.target.value })} placeholder="Appraiser name" /></label>
                    <label>
                      <span>Appraised Value</span>
                      <input value={valuationDrafts[side].appraisedValue ?? ''} onChange={(event) => patchValuationDraft(side, { appraisedValue: event.target.value === '' ? null : Number(event.target.value.replaceAll(',', '')) })} placeholder="$" />
                    </label>
                    <label><span>Value Date</span><input type="date" value={valuationDrafts[side].valueDate || ''} onChange={(event) => patchValuationDraft(side, { valueDate: event.target.value })} /></label>
                    <label className="full-span"><span>Methodology</span><textarea value={valuationDrafts[side].methodology || ''} onChange={(event) => patchValuationDraft(side, { methodology: event.target.value })} placeholder="Approach used, key comps, adjustments" /></label>
                    <label className="full-span"><span>Notes</span><textarea value={valuationDrafts[side].notes || ''} onChange={(event) => patchValuationDraft(side, { notes: event.target.value })} placeholder="Additional notes" /></label>
                  </div>
                  <div className="button-row compact-actions top-gap-small">
                    <button className="primary" onClick={() => void saveValuationPosition(side)}>Save {side} Position</button>
                  </div>
                  {valuationDrafts[side].updatedAt && <p className="helper-text top-gap-small">Last updated {displayDate(valuationDrafts[side].updatedAt)}.</p>}
                  </> : <div className="compact-empty-state"><p>No {side} position entered.</p><button className="primary" onClick={() => setEditingValuationSide(side)}>Add {side} Position</button></div>}

                  <div className="button-row compact-actions top-gap">
                    {valuationDrafts[side].appraisedValue != null && editingValuationSide !== side && <button onClick={() => setEditingValuationSide(side)}>Edit Position</button>}
                    <button onClick={() => startComparableSaleModal(side)}>Add Comparable Sale</button>
                  </div>
                  {renderComparableSalesTable(comparableSales.filter((sale) => sale.side === side))}
                </CollapsiblePanel>
              ))}
            </div>
          </div>
        )}

        {caseTab === 'trialNotebook' && (
          <div className="trial-notebook-grid">
            <div className="trial-materials-column">
            <Panel title="Witnesses">
              <div className="button-row compact-actions top-gap-small">
                {witnesses.length > 0 && <button className="primary" onClick={() => startWitnessModal()}>Add Witness</button>}
              </div>
              {witnesses.length === 0 ? <div className="compact-empty-state top-gap-small"><p>No witnesses added yet.</p><button className="primary" onClick={() => startWitnessModal()}>Add Witness</button></div> : <div className="table-wrap top-gap-small">
                <table className="compact-table">
                  <thead>
                    <tr>
                      <th>Name</th>
                      <th>Side</th>
                      <th>Role</th>
                      <th>Subpoena</th>
                      <th>Notes</th>
                      <th>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {witnesses.length === 0 ? (
                      <tr><td colSpan={6}>No witnesses added yet.</td></tr>
                    ) : witnesses.map((item) => (
                      <tr key={item.id}>
                        <td>{item.name}</td>
                        <td><span className={`pill pill-${sidePillTone(item.side)}`}>{item.side}</span></td>
                        <td>{item.role || 'Not set'}</td>
                        <td>{item.subpoenaStatus}</td>
                        <td>{item.notes || 'No notes'}</td>
                        <td>
                          <div className="button-row compact-actions row-actions">
                            <button onClick={() => startWitnessModal(item)}>Edit</button>
                            <button onClick={() => void deleteWitness(item.id)}>Delete</button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>}
            </Panel>

            <Panel title="Exhibits">
              <div className="button-row compact-actions top-gap-small">
                {exhibits.length > 0 && <button className="primary" onClick={() => startExhibitModal()}>Add Exhibit</button>}
              </div>
              {exhibits.length === 0 ? <div className="compact-empty-state top-gap-small"><p>No exhibits added yet.</p><button className="primary" onClick={() => startExhibitModal()}>Add Exhibit</button></div> : <div className="table-wrap top-gap-small">
                <table className="compact-table">
                  <thead>
                    <tr>
                      <th>Label</th>
                      <th>Side</th>
                      <th>Status</th>
                      <th>Description</th>
                      <th>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {exhibits.length === 0 ? (
                      <tr><td colSpan={5}>No exhibits added yet.</td></tr>
                    ) : exhibits.map((item) => (
                      <tr key={item.id}>
                        <td>{item.label}</td>
                        <td><span className={`pill pill-${sidePillTone(item.side)}`}>{item.side}</span></td>
                        <td>{item.status}</td>
                        <td>{item.description || 'No description'}</td>
                        <td>
                          <div className="button-row compact-actions row-actions">
                            <button onClick={() => startExhibitModal(item)}>Edit</button>
                            <button onClick={() => void deleteExhibit(item.id)}>Delete</button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>}
            </Panel>

            <Panel title="Trial Motions">
              <div className="button-row compact-actions top-gap-small">
                {trialMotions.length > 0 && <button className="primary" onClick={() => startTrialMotionModal()}>Add Trial Motion</button>}
              </div>
              {trialMotions.length === 0 ? <div className="compact-empty-state top-gap-small"><p>No trial motions added yet.</p><button className="primary" onClick={() => startTrialMotionModal()}>Add Trial Motion</button></div> : <div className="table-wrap top-gap-small">
                <table className="compact-table">
                  <thead>
                    <tr>
                      <th>Title</th>
                      <th>Filed By</th>
                      <th>Filed Date</th>
                      <th>Status</th>
                      <th>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {trialMotions.length === 0 ? (
                      <tr><td colSpan={5}>No trial motions added yet.</td></tr>
                    ) : trialMotions.map((item) => (
                      <tr key={item.id}>
                        <td>{item.title}</td>
                        <td><span className={`pill pill-${sidePillTone(item.filedBy)}`}>{item.filedBy}</span></td>
                        <td>{displayDate(item.filedDate)}</td>
                        <td>{item.status}</td>
                        <td>
                          <div className="button-row compact-actions row-actions">
                            <button onClick={() => startTrialMotionModal(item)}>Edit</button>
                            <button onClick={() => void deleteTrialMotion(item.id)}>Delete</button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>}
            </Panel>
            </div>
            <div className="trial-checklist-column">
            <Panel title="Trial Prep Checklist">
              <p className="helper-text">Trial-phase tasks stay editable here so you can update due dates, notes, and status without leaving the notebook.</p>
              {workspace ? (workspace.checklistItems.filter((item) => item.phase === 'Trial Preparation' || item.phase === 'Trial').length === 0 ? <div className="compact-empty-state"><p>No trial-prep tasks added yet.</p><div className="button-row compact-actions"><button className="primary" onClick={() => startChecklistModal()}>Add Task</button><button onClick={() => void openWorkTemplatePicker('Task')}>Add From Template</button></div></div> : renderChecklistTable(workspace.checklistItems.filter((item) => item.phase === 'Trial Preparation' || item.phase === 'Trial'), false, workspace.checklistItems, false)) : <p>Save the case first.</p>}
            </Panel>
            </div>
          </div>
        )}

      </main>
    )
  }

  // Unified Work tab (redesign Step 4a): one table replaces the old Deadlines, Tasks, and Events
  // tabs. Rows merge workspace.deadlines + checklistItems + hearings, grouped under phase headers
  // with progress bars; the facet chips replace the old per-tab open/done chip pairs.
  function renderWorkTab() {
    if (!workspace) {
      return (
        <div className="workspace-sections">
          <Panel title="Work">
            <p>Save the case first to manage deadlines, tasks, and events.</p>
          </Panel>
        </div>
      )
    }

    const deadlines = workspace.deadlines
    const tasks = workspace.checklistItems
    const events = workspace.hearings
    const openDeadlines = deadlines.filter((item) => !isDeadlineDone(item))
    const doneDeadlines = deadlines.filter(isDeadlineDone)
    const openTasks = tasks.filter((item) => !isChecklistDone(item))
    const doneTasks = tasks.filter(isChecklistDone)

    // Facet → visible rows. Open = open deadlines + open tasks + ALL events (events have no
    // done-ness); Deadlines/Tasks narrow to that type's open items; Done = done deadlines + tasks.
    const visibleDeadlines = workFacet === 'open' || workFacet === 'deadlines' ? openDeadlines : workFacet === 'done' ? doneDeadlines : []
    const visibleTasks = workFacet === 'open' || workFacet === 'tasks' ? openTasks : workFacet === 'done' ? doneTasks : []
    const visibleEvents = workFacet === 'open' || workFacet === 'events' ? events : []

    const facets: { key: typeof workFacet; label: string; count: number }[] = [
      { key: 'open', label: 'Open', count: openDeadlines.length + openTasks.length + events.length },
      { key: 'deadlines', label: 'Deadlines', count: openDeadlines.length },
      { key: 'tasks', label: 'Tasks', count: openTasks.length },
      { key: 'events', label: 'Events', count: events.length },
      { key: 'done', label: 'Done', count: doneDeadlines.length + doneTasks.length },
    ]
    const switchFacet = (facet: typeof workFacet) => {
      setWorkFacet(facet)
      setSelectedDeadlineIds([])
      setSelectedChecklistIds([])
      setBulkDeadlineDueDate('')
      setBulkChecklistDueDate('')
      setBulkChecklistDueDateOpen(false)
    }

    // ---- grouping ----
    const deadlineGroupKey = (item: DeadlineItem) => item.sourceStage || 'Court & Service Deadlines'
    const taskGroupKey = (item: ChecklistItem) => item.phase || 'General'

    // Totals per group come from the full set of that type (matching the old checklist table,
    // which showed x/y done across open + done items even in the Open view).
    const deadlineTotals = new Map<string, { done: number; total: number }>()
    for (const item of deadlines) {
      const entry = deadlineTotals.get(deadlineGroupKey(item)) ?? { done: 0, total: 0 }
      entry.total += 1
      if (isDeadlineDone(item)) entry.done += 1
      deadlineTotals.set(deadlineGroupKey(item), entry)
    }
    const taskTotals = new Map<string, { done: number; total: number }>()
    for (const item of tasks) {
      const entry = taskTotals.get(taskGroupKey(item)) ?? { done: 0, total: 0 }
      entry.total += 1
      if (isChecklistDone(item)) entry.done += 1
      taskTotals.set(taskGroupKey(item), entry)
    }

    const deadlineGroups = new Map<string, DeadlineItem[]>()
    for (const item of visibleDeadlines) {
      const key = deadlineGroupKey(item)
      deadlineGroups.set(key, [...(deadlineGroups.get(key) ?? []), item])
    }
    const taskGroups = new Map<string, ChecklistItem[]>()
    for (const item of sortChecklistForDisplay(visibleTasks)) {
      const key = taskGroupKey(item)
      taskGroups.set(key, [...(taskGroups.get(key) ?? []), item])
    }
    const orderedDeadlineGroups = [...deadlineGroups.entries()].sort((a, b) => phaseRank(a[0]) - phaseRank(b[0]) || a[0].localeCompare(b[0]))
    const orderedTaskGroups = [...taskGroups.entries()].sort((a, b) => phaseRank(a[0]) - phaseRank(b[0]) || a[0].localeCompare(b[0]))
    const sortedEvents = [...visibleEvents].sort((a, b) => (a.hearingDate || '9999-12-31').localeCompare(b.hearingDate || '9999-12-31'))

    // ---- bulk selection (spans both selectable kinds) ----
    const selDeadlines = visibleDeadlines.filter((item) => selectedDeadlineIds.includes(item.id))
    const selTasks = visibleTasks.filter((item) => selectedChecklistIds.includes(item.id))
    const selCount = selDeadlines.length + selTasks.length
    const selBreakdown = [
      selDeadlines.length > 0 ? `${selDeadlines.length} deadline${selDeadlines.length === 1 ? '' : 's'}` : null,
      selTasks.length > 0 ? `${selTasks.length} task${selTasks.length === 1 ? '' : 's'}` : null,
    ].filter(Boolean).join(', ')
    const runBulk = (action: 'complete' | 'reopen' | 'dueDate' | 'delete') => {
      if (selDeadlines.length > 0) void applyBulkDeadlineAction(action, visibleDeadlines)
      if (selTasks.length > 0) void applyBulkChecklistAction(action, visibleTasks)
    }
    const clearWorkSelection = () => {
      setSelectedDeadlineIds([])
      setSelectedChecklistIds([])
    }

    const emptyCopy: Record<typeof workFacet, { title: string; hint: string }> = {
      open: { title: 'No open work on this case', hint: 'Add a deadline, task, or event — or pull items in from a template.' },
      deadlines: { title: 'No open deadlines', hint: 'Court-imposed dates and legally significant deadlines will appear here.' },
      tasks: { title: 'No open tasks', hint: 'Internal action items and to-dos to keep the case moving.' },
      events: { title: 'No events logged yet', hint: 'Hearings, depositions, and anything else worth logging - a quick reference alongside your calendaring system.' },
      done: { title: 'Nothing completed yet', hint: 'Completed deadlines and tasks keep their completion timestamp and land here.' },
    }
    const hasRows = visibleDeadlines.length + visibleTasks.length + sortedEvents.length > 0

    const renderDeadlineRow = (item: DeadlineItem) => (
      <tr key={`deadline-${item.id}`} className={selectedDeadlineIds.includes(item.id) ? 'ui-row-sel' : undefined}>
        <td className="selection-cell">
          <input type="checkbox" checked={selectedDeadlineIds.includes(item.id)} onChange={() => toggleSelectedDeadline(item.id)} aria-label={`Select deadline ${item.title}`} />
        </td>
        <td><TypeChip kind="deadline" /></td>
        <td>
          <button className="ui-case-link" onClick={() => startDeadlineModal(item)}>{item.title}</button>
          {item.history && item.history.length > 0 && <div className="ui-sub">Originally due {displayDate(item.history[0].previousDueDate)}</div>}
          {item.completedAt && <div className="ui-sub">Completed {displayDateTime(item.completedAt)}</div>}
          <div className="ui-sub">Source: {item.sourceKind || item.sourceType}{item.sourceStage ? ` · ${item.sourceStage}` : ''}</div>
        </td>
        <td>
          <input type="date" className="inline-edit-input" value={item.dueDate || ''} aria-label={`Due date for ${item.title}`} onChange={(event) => void persistDeadline({ ...item, dueDate: event.target.value }, 'Due date updated.', false)} />
        </td>
        <td>
          <div className="work-status-cell">
            <StatusSelect value={item.status} options={deadlineStatuses} tone={deadlineRowTone(item)} ariaLabel={`Status for ${item.title}`} onChange={(value) => void persistDeadline({ ...item, status: value }, 'Deadline status updated.', false)} />
            <select className="inline-edit-select" value={item.severity || 'normal'} aria-label={`Severity for ${item.title}`} onChange={(event) => void persistDeadline({ ...item, severity: event.target.value }, 'Deadline severity updated.', false)}>
              {deadlineSeverities.map((level) => <option key={level} value={level}>{level}</option>)}
            </select>
          </div>
        </td>
        <td>
          <div className="ui-row-actions">
            {!isDeadlineDone(item) && (
              <button className="row-icon-button" title="Mark done" aria-label={`Mark deadline ${item.title} done`} onClick={() => void persistDeadline({ ...item, status: 'Done' }, 'Deadline marked done.', false)}>✓</button>
            )}
            <button className="row-icon-button" aria-label={`Delete deadline ${item.title}`} onClick={() => void deleteDeadline(item)}>✕</button>
          </div>
        </td>
      </tr>
    )

    const renderTaskRow = (item: ChecklistItem) => {
      const isDone = isChecklistDone(item)
      return (
        <tr key={`task-${item.id}`} className={selectedChecklistIds.includes(item.id) ? 'ui-row-sel' : isDone ? 'muted-row' : undefined}>
          <td className="selection-cell">
            <input type="checkbox" checked={selectedChecklistIds.includes(item.id)} onChange={() => toggleSelectedChecklist(item.id)} aria-label={`Select checklist item ${item.task}`} />
          </td>
          <td><TypeChip kind="task" /></td>
          <td>
            {isDone && <span className="status-icon" aria-hidden="true">✓</span>}
            <button className="ui-case-link" style={isDone ? { textDecoration: 'line-through' } : undefined} onClick={() => startChecklistModal(item)}>{item.task}</button>
            {item.completedAt && <div className="ui-sub">Completed {displayDateTime(item.completedAt)}</div>}
            <div className="ui-sub">Source: {item.sourceKind || item.sourceType}{item.sourceStage ? ` · ${item.sourceStage}` : ''}</div>
          </td>
          <td>
            <input type="date" className="inline-edit-input" value={item.dueDate || ''} aria-label={`Due date for ${item.task}`} onChange={(event) => void persistChecklist({ ...item, dueDate: event.target.value }, 'Due date updated.', false)} />
          </td>
          <td>
            <StatusSelect value={item.status} options={checklistStatuses} tone={checklistRowTone(item)} ariaLabel={`Status for ${item.task}`} onChange={(value) => void persistChecklist({ ...item, status: value }, 'Checklist status updated.', false)} />
          </td>
          <td>
            <div className="ui-row-actions">
              {!isDone && (
                <button className="row-icon-button" title="Mark done" aria-label={`Mark task ${item.task} done`} onClick={() => void persistChecklist({ ...item, status: 'Done' }, 'Task marked done.', false)}>✓</button>
              )}
              <button className="row-icon-button" aria-label={`Delete checklist item ${item.task}`} onClick={() => void deleteChecklistItem(item)}>✕</button>
            </div>
          </td>
        </tr>
      )
    }

    const renderEventRow = (hearing: Hearing) => (
      <tr key={`event-${hearing.id}`}>
        <td className="selection-cell" />
        <td><TypeChip kind="event" /></td>
        <td>
          {hearing.title}
          <div className="ui-sub">{hearing.eventType || 'Hearing'}{hearing.location ? ` · ${hearing.location}` : ''}</div>
          {hearing.description && <div className="ui-sub work-event-desc" title={hearing.description}>{hearing.description}</div>}
        </td>
        <td className="ui-data">{displayDate(hearing.hearingDate)}</td>
        <td><StatusChip tone="neutral">Scheduled</StatusChip></td>
        <td>
          <div className="ui-row-actions">
            <button className="row-icon-button" title="Edit event" aria-label={`Edit event ${hearing.title}`} onClick={() => startEditHearing(hearing)}>✎</button>
            <button className="row-icon-button" aria-label={`Delete event ${hearing.title}`} onClick={() => void deleteHearing(hearing)}>✕</button>
          </div>
        </td>
      </tr>
    )

    const renderGroupHeader = (key: string, label: string, kind: 'deadline' | 'task' | 'event', groupItems: Array<{ id: number }>, totals?: { done: number; total: number }) => {
      const pct = totals && totals.total > 0 ? Math.round((totals.done / totals.total) * 100) : 0
      const selectedIdSet = kind === 'deadline' ? selectedDeadlineIds : selectedChecklistIds
      const groupAllSelected = groupItems.length > 0 && groupItems.every((item) => selectedIdSet.includes(item.id))
      return (
        <tr key={`group-${kind}-${key}`} className="phase-row">
          <td colSpan={6}>
            <div className="phase-row-header">
              {kind === 'event' ? (
                <span className="phase-select-all">{label}</span>
              ) : (
                <label className="bulk-select-all phase-select-all">
                  <input
                    type="checkbox"
                    checked={groupAllSelected}
                    onChange={(event) => kind === 'task'
                      ? setPhaseSelectedChecklist(groupItems as ChecklistItem[], event.target.checked)
                      : setAllSelectedDeadlines(groupItems as DeadlineItem[], event.target.checked)}
                    aria-label={`Select all ${label} items`}
                  />
                  <span>{label}</span>
                </label>
              )}
              <span>{kind === 'event' ? `${groupItems.length} event${groupItems.length === 1 ? '' : 's'}` : `${totals?.done ?? 0} / ${totals?.total ?? 0} done`}</span>
            </div>
            {kind !== 'event' && <div className="progress-track"><div className="progress-fill" style={{ width: `${pct}%` }} /></div>}
          </td>
        </tr>
      )
    }

    const rows: ReactNode[] = []
    for (const [key, groupItems] of orderedDeadlineGroups) {
      rows.push(renderGroupHeader(key, key, 'deadline', groupItems, deadlineTotals.get(key)))
      for (const item of groupItems) rows.push(renderDeadlineRow(item))
    }
    for (const [key, groupItems] of orderedTaskGroups) {
      rows.push(renderGroupHeader(key, key, 'task', groupItems, taskTotals.get(key)))
      for (const item of groupItems) rows.push(renderTaskRow(item))
    }
    if (sortedEvents.length > 0) {
      rows.push(renderGroupHeader('events', 'Events', 'event', sortedEvents))
      for (const hearing of sortedEvents) rows.push(renderEventRow(hearing))
    }

    return (
      <div className="workspace-sections">
        <div className="work-toolbar">
          <Btn variant="primary" onClick={() => startDeadlineModal()}>Add deadline</Btn>
          <Btn onClick={() => startChecklistModal()}>Add task</Btn>
          <Btn onClick={startNewHearing}>Add event</Btn>
          {!workFromTemplateOpen ? (
            <Btn onClick={() => setWorkFromTemplateOpen(true)} aria-expanded={workFromTemplateOpen}>From template…</Btn>
          ) : (
            <span className="button-row compact-actions">
              <Btn size="sm" onClick={() => { setWorkFromTemplateOpen(false); void openWorkTemplatePicker('Deadline') }}>Deadline template</Btn>
              <Btn size="sm" onClick={() => { setWorkFromTemplateOpen(false); void openWorkTemplatePicker('Task') }}>Task template</Btn>
              <Btn size="sm" variant="ghost" onClick={() => setWorkFromTemplateOpen(false)}>Cancel</Btn>
            </span>
          )}
          <FilterSep />
          {facets.map((facet) => (
            <FilterChip key={facet.key} active={workFacet === facet.key} onClick={() => switchFacet(facet.key)}>
              {facet.label} · {facet.count}
            </FilterChip>
          ))}
          <label className="work-trial-date">
            <span>Trial / hearing date</span>
            <input
              type="date"
              value={selectedCase.trialDate || ''}
              onChange={(event) => void persistCasePatch({ trialDate: event.target.value }, 'Trial / hearing date updated.')}
            />
          </label>
        </div>

        {selCount > 0 && (
          <div className="ui-bulkbar" role="status">
            <span className="n">{selCount} selected{selBreakdown ? ` (${selBreakdown})` : ''}</span>
            <Btn size="sm" onClick={() => runBulk('complete')}>Mark done</Btn>
            <Btn size="sm" onClick={() => runBulk('reopen')}>Reopen</Btn>
            <Btn size="sm" onClick={() => setBulkChecklistDueDateOpen((open) => !open)}>Change due date</Btn>
            {bulkChecklistDueDateOpen && (
              <span className="bulk-date-popover">
                <input
                  type="date"
                  value={bulkChecklistDueDate}
                  autoFocus
                  aria-label="New due date for selected items"
                  onChange={(event) => { setBulkChecklistDueDate(event.target.value); setBulkDeadlineDueDate(event.target.value) }}
                />
                <button onClick={() => { setBulkChecklistDueDateOpen(false); runBulk('dueDate') }} disabled={!bulkChecklistDueDate}>Apply</button>
                <button onClick={() => setBulkChecklistDueDateOpen(false)}>Cancel</button>
              </span>
            )}
            <Btn size="sm" onClick={() => runBulk('delete')}>Delete</Btn>
            <Btn size="sm" variant="ghost" className="ui-bulkbar-clear" onClick={clearWorkSelection}>Clear</Btn>
          </div>
        )}

        <div className="ui-table-panel">
          <div className="table-wrap">
            <table className="ui-table">
              <thead>
                <tr>
                  <th className="selection-cell"><span className="visually-hidden">Select</span></th>
                  <th style={{ width: 90 }}>Type</th>
                  <th>Item</th>
                  <th style={{ width: 150 }}>Due</th>
                  <th style={{ width: 230 }}>Status</th>
                  <th style={{ width: 70 }}></th>
                </tr>
              </thead>
              <tbody>
                {hasRows ? rows : (
                  <UiEmptyState
                    colSpan={6}
                    title={emptyCopy[workFacet].title}
                    hint={emptyCopy[workFacet].hint}
                    action={workFacet !== 'done' ? <Btn size="sm" variant="primary" onClick={() => (workFacet === 'tasks' ? startChecklistModal() : workFacet === 'events' ? startNewHearing() : startDeadlineModal())}>{workFacet === 'tasks' ? 'Add task' : workFacet === 'events' ? 'Add event' : 'Add deadline'}</Btn> : undefined}
                  />
                )}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    )
  }

  function renderChecklistTable(items: ChecklistItem[], compact: boolean, totalsItems?: ChecklistItem[], showBulkToolbar = true, showCase = false) {
    const sorted = sortChecklistForDisplay(items)
    const columnCount = compact ? 4 : (showCase ? 6 : 5)
    const allSelected = items.length > 0 && items.every((item) => selectedChecklistIds.includes(item.id))
    const selectedCount = items.filter((item) => selectedChecklistIds.includes(item.id)).length
    const phaseTotals = new Map<string, { done: number; total: number }>()
    for (const item of (totalsItems ?? items)) {
      const phaseLabel = item.phase || 'General'
      const entry = phaseTotals.get(phaseLabel) ?? { done: 0, total: 0 }
      entry.total += 1
      if (isChecklistDone(item)) entry.done += 1
      phaseTotals.set(phaseLabel, entry)
    }
    const phaseVisibleItems = new Map<string, ChecklistItem[]>()
    for (const item of sorted) {
      const phaseLabel = item.phase || 'General'
      const list = phaseVisibleItems.get(phaseLabel) ?? []
      list.push(item)
      phaseVisibleItems.set(phaseLabel, list)
    }

    const rows: ReactNode[] = []
    let lastPhase: string | null = null
    for (const item of sorted) {
      const phaseLabel = item.phase || 'General'
      const isDone = isChecklistDone(item)
      if (!compact && phaseLabel !== lastPhase) {
        lastPhase = phaseLabel
        const totals = phaseTotals.get(phaseLabel)
        const pct = totals && totals.total > 0 ? Math.round((totals.done / totals.total) * 100) : 0
        const phaseItems = phaseVisibleItems.get(phaseLabel) ?? []
        const phaseAllSelected = phaseItems.length > 0 && phaseItems.every((phaseItem) => selectedChecklistIds.includes(phaseItem.id))
        rows.push(
          <tr key={`phase-${phaseLabel}`} className="phase-row">
            <td colSpan={columnCount}>
              <div className="phase-row-header">
                <label className="bulk-select-all phase-select-all">
                  <input
                    type="checkbox"
                    checked={phaseAllSelected}
                    onChange={(event) => setPhaseSelectedChecklist(phaseItems, event.target.checked)}
                    aria-label={`Select all ${phaseLabel} checklist items`}
                  />
                  <span>{phaseLabel}</span>
                </label>
                <span>{totals?.done ?? 0} / {totals?.total ?? 0} done</span>
              </div>
              <div className="progress-track"><div className="progress-fill" style={{ width: `${pct}%` }} /></div>
            </td>
          </tr>,
        )
      }

      rows.push(
        <tr key={item.id} className={isDone ? 'muted-row' : ''}>
          {!compact && (
            <td className="selection-cell">
              <input type="checkbox" checked={selectedChecklistIds.includes(item.id)} onChange={() => toggleSelectedChecklist(item.id)} aria-label={`Select checklist item ${item.task}`} />
            </td>
          )}
          {compact && <td>{phaseLabel}</td>}
          {showCase && <td><button className="link-button row-title-button" onClick={() => openCase(item.caseId, 'work')}>{dashboardCasesById.get(item.caseId)?.caseName || `Case ${item.caseId}`}</button></td>}
          <td>
            {isDone && <span className="status-icon" aria-hidden="true">✓</span>}
            {compact ? (
              <span style={isDone ? { textDecoration: 'line-through' } : undefined}>{item.task}</span>
            ) : (
              <button
                className="link-button row-title-button"
                style={isDone ? { textDecoration: 'line-through' } : undefined}
                onClick={() => startChecklistModal(item)}
              >
                {item.task}
              </button>
            )}
            {item.completedAt && <div className="flag-text muted">Completed {displayDateTime(item.completedAt)}</div>}
            <div className="flag-text muted">Source: {item.sourceKind || item.sourceType}{item.sourceStage ? ` · ${item.sourceStage}` : ''}</div>
          </td>
          <td>
            {compact ? displayDate(item.dueDate) : (
              <input
                type="date"
                className="inline-edit-input"
                value={item.dueDate || ''}
                onChange={(event) => void persistChecklist({ ...item, dueDate: event.target.value }, 'Due date updated.', false)}
              />
            )}
          </td>
          <td>
            {compact ? item.notes || 'No notes' : (
              <div className="button-row compact-actions row-actions">
                <select
                  className="inline-edit-select"
                  value={item.status}
                  onChange={(event) => void persistChecklist({ ...item, status: event.target.value }, 'Checklist status updated.', false)}
                >
                  {checklistStatuses.map((status) => <option key={status} value={status}>{status}</option>)}
                </select>
                {!isDone && (
                  <button className="row-icon-button" title="Mark done" aria-label={`Mark task ${item.task} done`} onClick={() => void persistChecklist({ ...item, status: 'Done' }, 'Task marked done.', false)}>✓</button>
                )}
              </div>
            )}
          </td>
          {!compact && (
            <td className="action-cell">
              <button className="row-icon-button" onClick={() => void deleteChecklistItem(item)} aria-label={`Delete checklist item ${item.task}`}>✕</button>
            </td>
          )}
        </tr>,
      )
    }

    return (
      <>
        {!compact && (showBulkToolbar || selectedCount > 0) && (
          <div className="bulk-action-bar">
            <div className="bulk-action-summary">
              <label className="bulk-select-all">
                <input type="checkbox" checked={allSelected} onChange={(event) => setAllSelectedChecklist(items, event.target.checked)} />
                <span>Select all visible</span>
              </label>
              <span className="helper-text">{selectedCount} selected</span>
            </div>
            <div className="bulk-action-controls">
              <button onClick={() => void applyBulkChecklistAction('complete', items)} disabled={selectedCount === 0}>Mark Done</button>
              <button onClick={() => void applyBulkChecklistAction('reopen', items)} disabled={selectedCount === 0}>Reopen</button>
              <button onClick={() => setBulkChecklistDueDateOpen((open) => !open)} disabled={selectedCount === 0}>Change Due Date</button>
              {bulkChecklistDueDateOpen && <span className="bulk-date-popover"><input type="date" value={bulkChecklistDueDate} onChange={(event) => setBulkChecklistDueDate(event.target.value)} autoFocus /><button onClick={() => { setBulkChecklistDueDateOpen(false); void applyBulkChecklistAction('dueDate', items) }} disabled={!bulkChecklistDueDate}>Apply</button><button onClick={() => setBulkChecklistDueDateOpen(false)}>Cancel</button></span>}
              <button onClick={() => setSelectedChecklistIds([])} disabled={selectedCount === 0}>Clear</button>
              <button onClick={() => void applyBulkChecklistAction('delete', items)} disabled={selectedCount === 0}>Delete</button>
            </div>
          </div>
        )}
        <div className="table-wrap">
          <table className={compact ? 'compact-table' : 'dense-table'}>
            <thead>
              <tr>
                {!compact && <th className="selection-cell">Select</th>}
                {compact && <th>Phase</th>}
                {showCase && <th>Case</th>}
                <th>Task</th>
                <th>Due Date</th>
                {compact ? <th>Notes</th> : <th>Status</th>}
                {!compact && <th className="action-cell-header">Delete</th>}
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 ? (
                <tr>
                  <td colSpan={columnCount}>No tasks for this case yet.</td>
                </tr>
              ) : rows}
            </tbody>
          </table>
        </div>
      </>
    )
  }

  // Discovery items grouped by type in an accordion (one group open at a time), each item a
  // compact title+status-badge row that expands to full detail on click - replaces the old
  // always-expanded card grid that stacked every type and every field on screen at once.
  function renderDiscoveryAccordion(items: DiscoveryItem[]) {
    if (items.length === 0) {
      return <p>No discovery items for this case yet.</p>
    }

    const groups = new Map<string, DiscoveryItem[]>()
    for (const item of items) {
      const key = (item.discoveryType || 'Uncategorized').trim() || 'Uncategorized'
      const bucketKey = [...groups.keys()].find((existing) => existing.toLowerCase() === key.toLowerCase()) ?? key
      groups.set(bucketKey, [...(groups.get(bucketKey) ?? []), item])
    }

    const statusRank = (status: string) => (status.includes('Follow-Up') ? 0 : status.includes('Waiting') ? 1 : status === 'Reopened' ? 2 : 3)

    return (
      <div className="stacked-panels compact-stack">
        {[...groups.entries()].map(([groupName, groupItems]) => {
          const worst = [...groupItems].sort((a, b) => statusRank(a.status) - statusRank(b.status))[0]
          const isOpen = expandedDiscoveryGroup === groupName
          return (
            <div key={groupName} className="panel reference-doc">
              <button
                type="button"
                className="discovery-group-header"
                onClick={() => { setExpandedDiscoveryGroup(isOpen ? null : groupName); setExpandedDiscoveryItemId(null) }}
              >
                <strong>{groupName}</strong>
                <span className="flag-text muted">{groupItems.length} item{groupItems.length === 1 ? '' : 's'}</span>
                <span className={`pill pill-${discoveryStatusPillTone(worst.status)}`}>{worst.status}</span>
                <span className="flag-text muted">{isOpen ? '▾' : '▸'}</span>
              </button>
              {isOpen && (
                <div className="panel-body">
                  {groupItems.map((item) => (
                    <div key={item.id} className="command-list-row-compact">
                      <div className="clickable-row button-row split-row" onClick={() => setExpandedDiscoveryItemId(expandedDiscoveryItemId === item.id ? null : item.id)}>
                        <div><strong>{item.requestTitle || `${item.direction} ${item.discoveryType}`}</strong><div className="flag-text muted">{item.direction} · Served {displayDate(item.servedDate)} · Due {displayDate(item.dueDate)} · Follow-up {displayDate(item.followUpDate)}</div></div>
                        <div className="button-row compact-actions row-actions" onClick={(event) => event.stopPropagation()}>
                          <select
                            className={`inline-edit-select pill-select pill-${discoveryStatusPillTone(item.status)}`}
                            value={item.status}
                            aria-label="Discovery status"
                            onChange={(event) => void persistDiscovery({ ...item, status: event.target.value }, 'Discovery status updated.', false)}
                          >
                            {discoveryStatuses.map((status) => <option key={status} value={status}>{status}</option>)}
                          </select>
                          <button onClick={() => startDiscoveryModal(item)}>Edit</button>
                        </div>
                      </div>
                      {expandedDiscoveryItemId === item.id && (
                        <div className="top-gap-small">
                          <div className="case-card-fields compact-card-fields">
                            <label><span>Direction</span><strong>{item.direction}</strong></label>
                            <label><span>Served</span><strong>{displayDate(item.servedDate)}</strong></label>
                            <label><span>Due</span><strong>{displayDate(item.dueDate)}</strong></label>
                            <label><span>Response</span><strong>{displayDate(item.responseDate)}</strong></label>
                            <label><span>Follow-Up</span><strong>{displayDate(item.followUpDate)}</strong></label>
                          </div>
                          {item.escalationNote && <div className="inline-message warn">{item.escalationNote}</div>}
                          {item.notes && <p className="flag-text muted">{item.notes}</p>}
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>
          )
        })}
      </div>
    )
  }

  function renderComparableSalesTable(items: ComparableSale[]) {
    if (items.length === 0) return <div className="compact-empty-state top-gap-small"><p>No comparable sales added yet.</p></div>
    return (
      <div className="table-wrap top-gap-small">
        <table className="compact-table">
          <thead>
            <tr>
              <th>Sale</th>
              <th>Price</th>
              <th>Date</th>
              <th>Acres</th>
              <th>$/Acre</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr key={item.id}>
                <td>{item.saleDescription || 'Untitled sale'}</td>
                <td>{displayCurrency(item.salePrice)}</td>
                <td>{displayDate(item.saleDate)}</td>
                <td>{item.sizeAcres ?? 'Not set'}</td>
                <td>{item.salePrice && item.sizeAcres ? displayCurrency(item.salePrice / item.sizeAcres) : 'Not set'}</td>
                <td>
                  <div className="button-row compact-actions row-actions">
                    <button onClick={() => startComparableSaleModal(item.side, item)}>Edit</button>
                    <button onClick={() => void deleteComparableSale(item.id)}>Delete</button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    )
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        <div>
          <p className="eyebrow">ARDOT Legal Division</p>
          <h1>ARDOT Case Planner</h1>
        </div>
        <div className="topbar-actions">
          <form className="topbar-search" onSubmit={(event) => { setSearchDropdownOpen(false); submitGlobalSearch(event) }}>
            <input
              value={topbarSearch}
              onChange={(event) => setTopbarSearch(event.target.value)}
              onFocus={() => { if (searchSuggestions.length > 0) setSearchDropdownOpen(true) }}
              onBlur={() => window.setTimeout(() => setSearchDropdownOpen(false), 150)}
              onKeyDown={(event) => { if (event.key === 'Escape') setSearchDropdownOpen(false) }}
              placeholder="Search cases..."
              aria-label="Search cases"
            />
            <button type="submit">Search</button>
            {searchDropdownOpen && searchSuggestions.length > 0 && (
              <div className="search-suggestions">
                {searchSuggestions.map((match) => (
                  <button
                    key={match.id}
                    type="button"
                    className="search-suggestion"
                    onMouseDown={(event) => event.preventDefault()}
                    onClick={() => { setSearchDropdownOpen(false); setTopbarSearch(''); setSearchSuggestions([]); openCase(match.id, 'overview') }}
                  >
                    <strong>{match.caseName || match.caseNumber || `Case ${match.id}`}</strong>
                    <span>{[match.caseNumber, match.jobNumber && `Job ${match.jobNumber}`, match.tract && `Tract ${match.tract}`].filter(Boolean).join(' · ')}</span>
                  </button>
                ))}
              </div>
            )}
          </form>
        </div>
      </header>

      <nav className="nav-row">
        {navItems.map((item) => (
          <button
            key={item.key}
            className={item.key === page ? 'nav-button active' : 'nav-button'}
            onClick={() => {
              setPage(item.key)
              if (item.key === 'cases') goToCaseList()
            }}
          >
            {item.label}
          </button>
        ))}
      </nav>

      {errorMessage && <div className="error-banner">{errorMessage}</div>}

      {activeModal === 'case' && (
        <Drawer
          title={`${modalMode === 'create' ? 'Add' : 'Edit'} ${modalKindLabels.case}`}
          width={720}
          onClose={() => {
            if (modalDirty) {
              setMessage('Save your changes or use Cancel to discard them before closing.')
              return
            }
            cancelModal()
          }}
          footer={
            <div className="button-row compact-actions">
              <button className="primary" type="submit" form="case-editor-form">Save Case</button>
              <button type="button" onClick={cancelModal}>Cancel</button>
            </div>
          }
        >
          {modalErrorSummary && (
            <div className="inline-message error modal-message" role="alert">
              {modalErrorSummary}
            </div>
          )}

          <nav className="drawer-section-nav" aria-label="Jump to case editor section">
            {caseEditorSections.map((section) => (
              <button key={section.key} type="button" className="ui-chip" onClick={() => scrollToCaseEditorSection(section.key)}>
                {section.label}
              </button>
            ))}
          </nav>

          <form id="case-editor-form" className="case-editor-form" onSubmit={saveCase} noValidate>
            <section className="form-section" ref={(node) => { caseEditorSectionRefs.current.identity = node }}>
              <h4 className="form-section-heading">Identity</h4>
              <div className="form-section-grid">
                <label>
                  <span>Case Name</span>
                  <input value={caseDraft.caseName} onChange={(event) => patchCaseDraft({ caseName: event.target.value })} placeholder="Case name" required />
                  {modalFieldErrors.caseName && <small className="field-error">{modalFieldErrors.caseName}</small>}
                </label>
                <label>
                  <span>Case Number</span>
                  <input value={caseDraft.caseNumber} onChange={(event) => patchCaseDraft({ caseNumber: event.target.value })} placeholder="Not assigned until filed" />
                  {modalFieldErrors.caseNumber && <small className="field-error">{modalFieldErrors.caseNumber}</small>}
                </label>
                <label><span>Job Number</span><input value={caseDraft.jobNumber} onChange={(event) => patchCaseDraft({ jobNumber: event.target.value })} placeholder="Job number" /></label>
                <label><span>Tract</span><input value={caseDraft.tract} onChange={(event) => patchCaseDraft({ tract: event.target.value })} placeholder="Tract" /></label>
                <label>
                  <span>County</span>
                  <select value={caseDraft.county || ''} onChange={(event) => patchCaseDraft({ county: event.target.value })}>
                    <option value="">Select county</option>
                    {countyOptions(caseDraft.county).map((county) => (
                      <option key={county} value={county}>{county}</option>
                    ))}
                  </select>
                </label>
                <label><span>Project Name</span><input value={caseDraft.projectName || ''} onChange={(event) => patchCaseDraft({ projectName: event.target.value })} placeholder="e.g. Highway 5 Widening" /></label>
                <label>
                  <span>Case Status</span>
                  <select value={caseDraft.caseStatus || 'Pipeline'} onChange={(event) => patchCaseDraft({ caseStatus: event.target.value })}>
                    {consolidatedCaseStatuses.map((status) => <option key={status} value={status}>{status}</option>)}
                  </select>
                </label>
                {(caseDraft.caseStatus || 'Pipeline') === 'Pipeline' && (
                  <label><span>Current Holder</span><select value={caseDraft.currentHolder || 'Legal Assistant'} onChange={(event) => patchCaseDraft({ currentHolder: event.target.value })}><option>Legal Assistant</option><option>Attorney</option><option>Deputy Chief Counsel</option><option>Chief Counsel</option><option>Other</option></select></label>
                )}
              </div>
            </section>

            <section className="form-section" ref={(node) => { caseEditorSectionRefs.current.people = node }}>
              <h4 className="form-section-heading">People</h4>
              <div className="form-section-grid">
                <label><span>Landowner</span><input value={caseDraft.landowner || ''} onChange={(event) => patchCaseDraft({ landowner: event.target.value })} placeholder="Landowner" /></label>
                <label><span>Opposing Counsel</span><input value={caseDraft.opposingCounsel || ''} onChange={(event) => patchCaseDraft({ opposingCounsel: event.target.value })} placeholder="Opposing counsel" /></label>
                <label><span>Appraiser</span><input value={caseDraft.appraiser || ''} onChange={(event) => patchCaseDraft({ appraiser: event.target.value })} placeholder="Appraiser" /></label>
                <label><span>Landowner's Appraiser</span><input value={caseDraft.landownerAppraiserName || ''} onChange={(event) => patchCaseDraft({ landownerAppraiserName: event.target.value })} placeholder="Landowner's appraiser" /></label>
              </div>
            </section>

            <section className="form-section" ref={(node) => { caseEditorSectionRefs.current.dates = node }}>
              <h4 className="form-section-heading">Dates</h4>
              <div className="form-section-grid">
                <label>
                  <span>Filing Date</span>
                  <input type="date" value={caseDraft.filingDate || ''} onChange={(event) => patchCaseDraft({ filingDate: event.target.value })} onInput={(event) => patchCaseDraft({ filingDate: event.currentTarget.value })} />
                  {modalFieldErrors.filingDate && <small className="field-error">{modalFieldErrors.filingDate}</small>}
                </label>
                <label>
                  <span>Date Opened</span>
                  <input type="date" value={caseDraft.dateOpened || ''} onChange={(event) => patchCaseDraft({ dateOpened: event.target.value })} onInput={(event) => patchCaseDraft({ dateOpened: event.currentTarget.value })} />
                </label>
                <label>
                  <span>Date of Taking</span>
                  <input type="date" value={caseDraft.dateOfTaking || ''} onChange={(event) => patchCaseDraft({ dateOfTaking: event.target.value })} onInput={(event) => patchCaseDraft({ dateOfTaking: event.currentTarget.value })} />
                  {modalFieldErrors.dateOfTaking && <small className="field-error">{modalFieldErrors.dateOfTaking}</small>}
                </label>
                {(caseDraft.caseStatus || 'Pipeline') === 'Pipeline' && (
                  <label><span>Next Review Date</span><input type="date" value={caseDraft.nextReviewDate || ''} onChange={(event) => patchCaseDraft({ nextReviewDate: event.target.value })} /></label>
                )}
                {caseDraft.status === 'Closed' && (
                  <label>
                    <span>Closed Date</span>
                    <input type="date" value={caseDraft.closedDate || ''} onChange={(event) => patchCaseDraft({ closedDate: event.target.value })} onInput={(event) => patchCaseDraft({ closedDate: event.currentTarget.value })} />
                    {modalFieldErrors.closedDate && <small className="field-error">{modalFieldErrors.closedDate}</small>}
                  </label>
                )}
              </div>
            </section>

            <section className="form-section" ref={(node) => { caseEditorSectionRefs.current.financial = node }}>
              <h4 className="form-section-heading">Financial & Property</h4>
              <div className="form-section-grid">
                <label>
                  <span>Deposit Amount</span>
                  <NumericField money value={caseDraft.depositAmount} onCommit={(value) => patchCaseDraft({ depositAmount: value })} placeholder="Deposit amount" />
                  {modalFieldErrors.depositAmount && <small className="field-error">{modalFieldErrors.depositAmount}</small>}
                </label>
                <label className="toggle-inline"><span>Additional Deposit</span><input type="checkbox" checked={caseDraft.additionalDepositAmount != null || Boolean(caseDraft.additionalDepositDate)} onChange={(event) => patchCaseDraft(event.target.checked ? { additionalDepositAmount: caseDraft.additionalDepositAmount ?? 0 } : { additionalDepositAmount: null, additionalDepositDate: '' })} /></label>
                {((caseDraft.additionalDepositAmount != null) || Boolean(caseDraft.additionalDepositDate)) && (
                  <>
                    <label><span>Additional Deposit Amount</span><NumericField money value={caseDraft.additionalDepositAmount} onCommit={(value) => patchCaseDraft({ additionalDepositAmount: value })} placeholder="Additional deposit amount" /></label>
                    <label>
                      <span>Additional Deposit Date</span>
                      <input type="date" value={caseDraft.additionalDepositDate || ''} onChange={(event) => patchCaseDraft({ additionalDepositDate: event.target.value })} onInput={(event) => patchCaseDraft({ additionalDepositDate: event.currentTarget.value })} />
                    </label>
                  </>
                )}
                <label><span>Whole Property (acres)</span><NumericField value={caseDraft.wholePropertyAcres} onCommit={(value) => patchCaseDraft({ wholePropertyAcres: value })} placeholder="Whole property acres" /></label>
                <label><span>Acquisition (acres)</span><NumericField value={caseDraft.acquisitionAcres} onCommit={(value) => patchCaseDraft({ acquisitionAcres: value })} placeholder="Acquisition acres" /></label>
                <label className="toggle-inline"><span>Taxes Owed</span><input type="checkbox" checked={isYesLike(caseDraft.taxesOwed)} onChange={(event) => patchCaseDraft({ taxesOwed: event.target.checked ? 'Yes' : '', taxOwedAmount: event.target.checked ? caseDraft.taxOwedAmount : null })} /></label>
                {isYesLike(caseDraft.taxesOwed) && <label><span>Tax Amount Owed</span><NumericField money value={caseDraft.taxOwedAmount} onCommit={(value) => patchCaseDraft({ taxOwedAmount: value })} placeholder="Tax amount owed" /></label>}
                <label className="toggle-inline"><span>Funds Withdrawn</span><input type="checkbox" checked={isYesLike(caseDraft.fundsWithdrawn)} onChange={(event) => patchCaseDraft({ fundsWithdrawn: event.target.checked ? 'Yes' : '', fundsWithdrawnDate: event.target.checked ? caseDraft.fundsWithdrawnDate : '' })} /></label>
                {isYesLike(caseDraft.fundsWithdrawn) && (
                  <label>
                    <span>Funds Withdrawn Date</span>
                    <input type="date" value={caseDraft.fundsWithdrawnDate || ''} onChange={(event) => patchCaseDraft({ fundsWithdrawnDate: event.target.value })} onInput={(event) => patchCaseDraft({ fundsWithdrawnDate: event.currentTarget.value })} />
                  </label>
                )}
              </div>
            </section>

            <section className="form-section" ref={(node) => { caseEditorSectionRefs.current.service = node }}>
              <h4 className="form-section-heading">Service</h4>
              <p className="helper-text">Service perfection, deadlines, and publication entries are managed on the Status tab.</p>
            </section>

            <section className="form-section" ref={(node) => { caseEditorSectionRefs.current.notes = node }}>
              <h4 className="form-section-heading">Notes</h4>
              {(caseDraft.caseStatus || 'Pipeline') === 'Pipeline' && (
                <label><span>Pipeline Note</span><textarea rows={2} value={caseDraft.shortPostureSummary || ''} onChange={(event) => patchCaseDraft({ shortPostureSummary: event.target.value })} placeholder="Optional pleading-preparation or handoff note" /></label>
              )}
              <div>
                <h4>Issue Tags</h4>
                <div className="button-row compact-actions top-gap-small">
                  <select value={selectedTagId} onChange={(event) => setSelectedTagId(Number(event.target.value))}>
                    <option value={0}>Choose a tag</option>
                    {(workspace?.availableIssueTags ?? []).map((tag) => (
                      <option key={tag.id} value={tag.id}>{tag.name}</option>
                    ))}
                  </select>
                  <button type="button" onClick={() => void addIssueTag()}>Add Issue Tag</button>
                </div>
                {issueTagMessage && <p className="inline-message warn top-gap-small">{issueTagMessage}</p>}
                {workspace && workspace.caseIssueTags.length > 0 ? (
                  <ul className="plain-list top-gap-small">
                    {workspace.caseIssueTags.map((tag) => (
                      <li key={tag.id} className="list-row">
                        <span>{tag.tagName}{tag.category ? ` | ${tag.category}` : ''}</span>
                        <button type="button" onClick={() => void removeIssueTag(tag.id)}>Remove</button>
                      </li>
                    ))}
                  </ul>
                ) : <p className="top-gap-small">No issue tags assigned.</p>}
              </div>
            </section>
          </form>
        </Drawer>
      )}

      {activeModal && activeModal !== 'case' && (
        <ModalShell
          title={`${modalMode === 'create' ? 'Add' : 'Edit'} ${modalKindLabels[activeModal]}`}
          onClose={() => {
            if (modalDirty) {
              setMessage('Save your changes or use Cancel to discard them before closing.')
              return
            }
            cancelModal()
          }}
        >
          {modalErrorSummary && (
            <div className="inline-message error modal-message" role="alert">
              {modalErrorSummary}
            </div>
          )}

          {activeModal === 'deadline' && (
            <form className="form-grid modal-form" onSubmit={saveDeadline} noValidate>
              <label>
                <span>Title</span>
                <input value={deadlineDraft.title} onChange={(event) => patchDeadlineDraft({ title: event.target.value })} placeholder="Deadline title" required />
                {modalFieldErrors.title && <small className="field-error">{modalFieldErrors.title}</small>}
              </label>
              <label>
                <span>Due Date</span>
                <input type="date" value={deadlineDraft.dueDate || ''} onChange={(event) => patchDeadlineDraft({ dueDate: event.target.value })} onInput={(event) => patchDeadlineDraft({ dueDate: event.currentTarget.value })} />
                {modalFieldErrors.dueDate && <small className="field-error">{modalFieldErrors.dueDate}</small>}
              </label>
              <label><span>Status</span><select value={deadlineDraft.status} onChange={(event) => patchDeadlineDraft({ status: event.target.value })}>{deadlineStatuses.map((status) => <option key={status}>{status}</option>)}</select></label>
              <label>
                <span>Severity</span>
                <select value={deadlineDraft.severity || 'normal'} onChange={(event) => patchDeadlineDraft({ severity: event.target.value })}>
                  {deadlineSeverities.map((level) => <option key={level} value={level}>{level}</option>)}
                </select>
              </label>
              <label className="full-span"><span>Notes</span><textarea value={deadlineDraft.notes || ''} onChange={(event) => patchDeadlineDraft({ notes: event.target.value })} placeholder="Deadline notes" /></label>
              {modalMode === 'edit' && deadlineDraft.id !== 0 && (
                <label className="full-span">
                  <span>Reason for date change (optional, e.g. court order reference)</span>
                  <textarea value={deadlineDraft.reasonForChange || ''} onChange={(event) => patchDeadlineDraft({ reasonForChange: event.target.value })} placeholder="e.g. Motion for Extension granted 2026-07-28" />
                </label>
              )}
              {deadlineDraft.history && deadlineDraft.history.length > 0 && (
                <div className="full-span deadline-history">
                  <EditHistoryList
                    title="Date history"
                    rows={deadlineDraft.history.map((entry, index) => ({
                      id: index,
                      previous: displayDate(entry.previousDueDate),
                      next: displayDate(entry.newDueDate),
                      reason: entry.reason,
                      createdAt: entry.changedAt,
                    }))}
                  />
                </div>
              )}
              <div className="button-row compact-actions full-span modal-footer">
                <button className="primary" type="submit">Save Deadline</button>
                <button type="button" onClick={cancelModal}>Cancel</button>
              </div>
            </form>
          )}

          {activeModal === 'checklist' && (
            <form className="form-grid modal-form" onSubmit={saveChecklist} noValidate>
              <label>
                <span>Task / Title</span>
                <input value={checklistDraft.task} onChange={(event) => patchChecklistDraft({ task: event.target.value })} placeholder="Checklist task" required />
                {modalFieldErrors.task && <small className="field-error">{modalFieldErrors.task}</small>}
              </label>
              <label>
                <span>Phase</span>
                <select value={checklistDraft.phase || 'General'} onChange={(event) => patchChecklistDraft({ phase: event.target.value })}>
                  <option value="General">General</option>
                  {caseStages.map((stage) => <option key={stage} value={stage}>{stage}</option>)}
                </select>
              </label>
              <label><span>Status</span><select value={checklistDraft.status} onChange={(event) => patchChecklistDraft({ status: event.target.value })}>{checklistStatuses.map((status) => <option key={status}>{status}</option>)}</select></label>
              <label>
                <span>Due Date</span>
                <input type="date" value={checklistDraft.dueDate || ''} onChange={(event) => patchChecklistDraft({ dueDate: event.target.value })} onInput={(event) => patchChecklistDraft({ dueDate: event.currentTarget.value })} />
                {modalFieldErrors.dueDate && <small className="field-error">{modalFieldErrors.dueDate}</small>}
              </label>
              <label className="full-span"><span>Notes</span><textarea value={checklistDraft.notes || ''} onChange={(event) => patchChecklistDraft({ notes: event.target.value })} placeholder="Checklist notes" /></label>
              <div className="button-row compact-actions full-span modal-footer">
                <button className="primary" type="submit">Save Task</button>
                <button type="button" onClick={cancelModal}>Cancel</button>
              </div>
            </form>
          )}

          {activeModal === 'discovery' && (
            <form className="form-grid modal-form" onSubmit={saveDiscovery} noValidate>
              <label className="full-span">
                <span>Request Title</span>
                <input value={discoveryDraft.requestTitle || ''} onChange={(event) => patchDiscoveryDraft({ requestTitle: event.target.value })} placeholder="e.g. Landowner's First Set of Interrogatories" />
              </label>
              <label><span>Direction</span><select value={discoveryDraft.direction} onChange={(event) => patchDiscoveryDraft({ direction: event.target.value })}><option>Served by Us</option><option>Served on Us</option></select></label>
              <label>
                <span>Discovery Type</span>
                <input value={discoveryDraft.discoveryType} onChange={(event) => patchDiscoveryDraft({ discoveryType: event.target.value })} placeholder="Discovery type" />
                {modalFieldErrors.discoveryType && <small className="field-error">{modalFieldErrors.discoveryType}</small>}
              </label>
              <label><span>Status</span><select value={discoveryDraft.status} onChange={(event) => patchDiscoveryDraft({ status: event.target.value })}>{discoveryStatuses.map((status) => <option key={status}>{status}</option>)}</select></label>
              <label><span>Assigned To</span><input value={discoveryDraft.assignedTo || ''} onChange={(event) => patchDiscoveryDraft({ assignedTo: event.target.value })} placeholder="Assigned to" /></label>
              <label>
                <span>Served Date</span>
                <input type="date" value={discoveryDraft.servedDate || ''} onChange={(event) => patchDiscoveryDraft({ servedDate: event.target.value })} onInput={(event) => patchDiscoveryDraft({ servedDate: event.currentTarget.value })} />
                {modalFieldErrors.servedDate && <small className="field-error">{modalFieldErrors.servedDate}</small>}
              </label>
              <label>
                <span>Due Date</span>
                <input type="date" value={discoveryDraft.dueDate || ''} onChange={(event) => patchDiscoveryDraft({ dueDate: event.target.value })} onInput={(event) => patchDiscoveryDraft({ dueDate: event.currentTarget.value })} />
                {modalFieldErrors.dueDate && <small className="field-error">{modalFieldErrors.dueDate}</small>}
              </label>
              <label>
                <span>Response Date</span>
                <input type="date" value={discoveryDraft.responseDate || ''} onChange={(event) => patchDiscoveryDraft({ responseDate: event.target.value })} onInput={(event) => patchDiscoveryDraft({ responseDate: event.currentTarget.value })} />
                {modalFieldErrors.responseDate && <small className="field-error">{modalFieldErrors.responseDate}</small>}
              </label>
              <label>
                <span>Follow-Up Date</span>
                <input type="date" value={discoveryDraft.followUpDate || ''} onChange={(event) => patchDiscoveryDraft({ followUpDate: event.target.value })} onInput={(event) => patchDiscoveryDraft({ followUpDate: event.currentTarget.value })} />
                {modalFieldErrors.followUpDate && <small className="field-error">{modalFieldErrors.followUpDate}</small>}
              </label>
              <label className="full-span"><span>Notes</span><textarea value={discoveryDraft.notes || ''} onChange={(event) => patchDiscoveryDraft({ notes: event.target.value })} placeholder="Discovery notes" /></label>
              <label className="full-span">
                <span>Escalation Note</span>
                <textarea value={discoveryDraft.escalationNote || ''} onChange={(event) => patchDiscoveryDraft({ escalationNote: event.target.value })} placeholder="e.g. Good faith letter sent, no response - motion to compel may be needed" />
              </label>
              <div className="button-row compact-actions full-span modal-footer">
                <button className="primary" type="submit">Save Discovery Item</button>
                <button type="button" onClick={cancelModal}>Cancel</button>
              </div>
            </form>
          )}

          {activeModal === 'comparableSale' && (
            <form className="form-grid modal-form" onSubmit={saveComparableSale} noValidate>
              <label><span>Side</span><select value={comparableSaleDraft.side} onChange={(event) => patchComparableSaleDraft({ side: event.target.value as ValuationSide })}><option value="ASHC">ASHC</option><option value="Landowner">Landowner</option></select></label>
              <label className="full-span">
                <span>Sale Description</span>
                <input value={comparableSaleDraft.saleDescription || ''} onChange={(event) => patchComparableSaleDraft({ saleDescription: event.target.value })} placeholder="e.g. 12 acres, Hwy 65 frontage, Saline County" />
                {modalFieldErrors.saleDescription && <small className="field-error">{modalFieldErrors.saleDescription}</small>}
              </label>
              <label>
                <span>Sale Price</span>
                <input value={comparableSaleDraft.salePrice ?? ''} onChange={(event) => patchComparableSaleDraft({ salePrice: event.target.value === '' ? null : Number(event.target.value.replaceAll(',', '')) })} placeholder="$" />
              </label>
              <label>
                <span>Sale Date</span>
                <input type="date" value={comparableSaleDraft.saleDate || ''} onChange={(event) => patchComparableSaleDraft({ saleDate: event.target.value })} onInput={(event) => patchComparableSaleDraft({ saleDate: event.currentTarget.value })} />
              </label>
              <label>
                <span>Size (Acres)</span>
                <input value={comparableSaleDraft.sizeAcres ?? ''} onChange={(event) => patchComparableSaleDraft({ sizeAcres: event.target.value === '' ? null : Number(event.target.value.replaceAll(',', '')) })} placeholder="Acres" />
              </label>
              <label className="full-span"><span>Adjustment Notes</span><textarea value={comparableSaleDraft.adjustmentNotes || ''} onChange={(event) => patchComparableSaleDraft({ adjustmentNotes: event.target.value })} placeholder="Time, location, size, and other adjustments made to this comp" /></label>
              <label className="full-span"><span>Notes</span><textarea value={comparableSaleDraft.notes || ''} onChange={(event) => patchComparableSaleDraft({ notes: event.target.value })} placeholder="Additional notes" /></label>
              <div className="button-row compact-actions full-span modal-footer">
                <button className="primary" type="submit">Save Comparable Sale</button>
                <button type="button" onClick={cancelModal}>Cancel</button>
              </div>
            </form>
          )}

          {activeModal === 'witness' && (
            <form className="form-grid modal-form" onSubmit={saveWitness} noValidate>
              <label>
                <span>Name</span>
                <input value={witnessDraft.name} onChange={(event) => patchWitnessDraft({ name: event.target.value })} placeholder="Witness name" />
                {modalFieldErrors.name && <small className="field-error">{modalFieldErrors.name}</small>}
              </label>
              <label><span>Side</span><select value={witnessDraft.side} onChange={(event) => patchWitnessDraft({ side: event.target.value as ValuationSide })}><option value="ASHC">ASHC</option><option value="Landowner">Landowner</option></select></label>
              <label><span>Role</span><input value={witnessDraft.role || ''} onChange={(event) => patchWitnessDraft({ role: event.target.value })} placeholder="e.g. Appraiser, Fact Witness, Engineer" /></label>
              <label><span>Subpoena Status</span><select value={witnessDraft.subpoenaStatus} onChange={(event) => patchWitnessDraft({ subpoenaStatus: event.target.value })}>{subpoenaStatuses.map((status) => <option key={status}>{status}</option>)}</select></label>
              <label className="full-span"><span>Contact Info</span><input value={witnessDraft.contactInfo || ''} onChange={(event) => patchWitnessDraft({ contactInfo: event.target.value })} placeholder="Phone, email, or address" /></label>
              <label className="full-span"><span>Outline Notes</span><textarea value={witnessDraft.outlineNotes || ''} onChange={(event) => patchWitnessDraft({ outlineNotes: event.target.value })} placeholder="Direct/cross exam outline summary" /></label>
              <label className="full-span"><span>Notes</span><textarea value={witnessDraft.notes || ''} onChange={(event) => patchWitnessDraft({ notes: event.target.value })} placeholder="Additional notes" /></label>
              <div className="button-row compact-actions full-span modal-footer">
                <button className="primary" type="submit">Save Witness</button>
                <button type="button" onClick={cancelModal}>Cancel</button>
              </div>
            </form>
          )}

          {activeModal === 'exhibit' && (
            <form className="form-grid modal-form" onSubmit={saveExhibit} noValidate>
              <label>
                <span>Label</span>
                <input value={exhibitDraft.label} onChange={(event) => patchExhibitDraft({ label: event.target.value })} placeholder="e.g. Comps Map, ROW Map, Appraiser CV" />
                {modalFieldErrors.label && <small className="field-error">{modalFieldErrors.label}</small>}
              </label>
              <label><span>Side</span><select value={exhibitDraft.side} onChange={(event) => patchExhibitDraft({ side: event.target.value as ValuationSide })}><option value="ASHC">ASHC</option><option value="Landowner">Landowner</option></select></label>
              <label><span>Status</span><select value={exhibitDraft.status} onChange={(event) => patchExhibitDraft({ status: event.target.value })}>{exhibitStatuses.map((status) => <option key={status}>{status}</option>)}</select></label>
              <label className="full-span"><span>Description</span><textarea value={exhibitDraft.description || ''} onChange={(event) => patchExhibitDraft({ description: event.target.value })} placeholder="What this exhibit shows" /></label>
              <label className="full-span"><span>Notes</span><textarea value={exhibitDraft.notes || ''} onChange={(event) => patchExhibitDraft({ notes: event.target.value })} placeholder="Additional notes" /></label>
              <div className="button-row compact-actions full-span modal-footer">
                <button className="primary" type="submit">Save Exhibit</button>
                <button type="button" onClick={cancelModal}>Cancel</button>
              </div>
            </form>
          )}

          {activeModal === 'trialMotion' && (
            <form className="form-grid modal-form" onSubmit={saveTrialMotion} noValidate>
              <label className="full-span">
                <span>Title</span>
                <input value={trialMotionDraft.title} onChange={(event) => patchTrialMotionDraft({ title: event.target.value })} placeholder="e.g. Motion in Limine re: prior offers" />
                {modalFieldErrors.title && <small className="field-error">{modalFieldErrors.title}</small>}
              </label>
              <label><span>Filed By</span><select value={trialMotionDraft.filedBy} onChange={(event) => patchTrialMotionDraft({ filedBy: event.target.value as ValuationSide })}><option value="ASHC">ASHC</option><option value="Landowner">Landowner</option></select></label>
              <label><span>Status</span><select value={trialMotionDraft.status} onChange={(event) => patchTrialMotionDraft({ status: event.target.value })}>{motionStatuses.map((status) => <option key={status}>{status}</option>)}</select></label>
              <label>
                <span>Filed Date</span>
                <input type="date" value={trialMotionDraft.filedDate || ''} onChange={(event) => patchTrialMotionDraft({ filedDate: event.target.value })} onInput={(event) => patchTrialMotionDraft({ filedDate: event.currentTarget.value })} />
              </label>
              <label className="full-span"><span>Notes</span><textarea value={trialMotionDraft.notes || ''} onChange={(event) => patchTrialMotionDraft({ notes: event.target.value })} placeholder="Additional notes" /></label>
              <div className="button-row compact-actions full-span modal-footer">
                <button className="primary" type="submit">Save Trial Motion</button>
                <button type="button" onClick={cancelModal}>Cancel</button>
              </div>
            </form>
          )}

          {activeModal === 'event' && (
            <form className="form-grid modal-form" onSubmit={saveHearing} noValidate>
              <label>
                <span>Type</span>
                <select
                  value={(eventTypes as readonly string[]).includes(hearingDraft.eventType || '') ? hearingDraft.eventType || 'Hearing' : '__custom'}
                  onChange={(event) => setHearingDraft({ ...hearingDraft, eventType: event.target.value === '__custom' ? '' : event.target.value })}
                >
                  {eventTypes.map((type) => <option key={type} value={type}>{type}</option>)}
                  <option value="__custom">Custom…</option>
                </select>
              </label>
              {!(eventTypes as readonly string[]).includes(hearingDraft.eventType || '') && (
                <label><span>Custom Type</span><input value={hearingDraft.eventType || ''} onChange={(event) => setHearingDraft({ ...hearingDraft, eventType: event.target.value })} placeholder="e.g. Site Visit" /></label>
              )}
              <label className="full-span">
                <span>Title</span>
                <input value={hearingDraft.title} onChange={(event) => setHearingDraft({ ...hearingDraft, title: event.target.value })} placeholder="e.g. Motion Hearing, Trial" />
                {modalFieldErrors.title && <small className="field-error">{modalFieldErrors.title}</small>}
              </label>
              <label><span>Date</span><input type="date" value={hearingDraft.hearingDate || ''} onChange={(event) => setHearingDraft({ ...hearingDraft, hearingDate: event.target.value })} /></label>
              <label><span>Location</span><input value={hearingDraft.location || ''} onChange={(event) => setHearingDraft({ ...hearingDraft, location: event.target.value })} placeholder="Courtroom, address, or venue" /></label>
              <label className="full-span"><span>Notes</span><textarea value={hearingDraft.description || ''} onChange={(event) => setHearingDraft({ ...hearingDraft, description: event.target.value })} placeholder="What this event covers" /></label>
              <div className="button-row compact-actions full-span modal-footer">
                <button className="primary" type="submit">{hearingDraft.id === 0 ? 'Save Event' : 'Update Event'}</button>
                <button type="button" onClick={cancelModal}>Cancel</button>
              </div>
            </form>
          )}
        </ModalShell>
      )}

      {workTemplatePicker && (
        <ModalShell title="Review Task and Deadline Templates" onClose={() => setWorkTemplatePicker(null)}>
          {(() => {
            const scoped = workTemplatePicker.items.filter((x) => workTemplatePicker.kind === 'All' || x.kind === workTemplatePicker.kind)
            const visible = scoped.filter((x) => workTemplateFilter === 'all' || (workTemplateFilter === 'recommended' && !x.isDuplicate) || (workTemplateFilter === 'duplicates' && x.isDuplicate) || (workTemplateFilter === 'tasks' && x.kind === 'Task') || (workTemplateFilter === 'deadlines' && x.kind === 'Deadline'))
            const selectedCount = scoped.filter((x) => x.selected).length
            const duplicateCount = scoped.filter((x) => x.isDuplicate).length
            const recommendedCount = scoped.length - duplicateCount
            const updateItem = (item: WorkTemplateCandidate, changes: Partial<WorkTemplateCandidate>) => {
              const index = workTemplatePicker.items.indexOf(item)
              const items = [...workTemplatePicker.items]
              items[index] = { ...item, ...changes }
              setWorkTemplatePicker({ ...workTemplatePicker, items })
            }
            return <>
              <div className="template-review-summary">
                <div><strong>{recommendedCount}</strong><span>recommended</span></div>
                <div><strong>{duplicateCount}</strong><span>duplicates</span></div>
                <div><strong>{selectedCount}</strong><span>selected</span></div>
                <div className="template-review-source">Source: {workTemplatePicker.kind === 'All' ? 'status advancement' : `${workTemplatePicker.kind} templates`}</div>
              </div>
              <div className="button-row compact-actions template-review-toolbar">
                <button onClick={() => { const items = workTemplatePicker.items.map((x) => ({ ...x, selected: !x.isDuplicate })); setWorkTemplatePicker({ ...workTemplatePicker, items }) }}>Select All Recommended</button>
                <button onClick={() => setWorkTemplatePicker({ ...workTemplatePicker, items: workTemplatePicker.items.map((x) => ({ ...x, selected: false })) })}>Clear Selection</button>
                {(['recommended','all','duplicates','tasks','deadlines'] as const).map((filter) => <button key={filter} className={workTemplateFilter === filter ? 'selected-filter' : ''} onClick={() => setWorkTemplateFilter(filter)}>{filter === 'recommended' ? 'Recommended' : filter === 'all' ? 'Show All' : filter === 'duplicates' ? 'Show Duplicates' : filter === 'tasks' ? 'Show Tasks' : 'Show Deadlines'}</button>)}
              </div>
              <div className="template-review-list">
                {visible.map((item) => {
                  const key = `${item.kind}-${item.templateId}`
                  const expanded = expandedWorkTemplateId === key
                  return <div className="template-review-item" key={key}>
                    <div className="template-review-row">
                      <input aria-label={`Select ${item.title}`} type="checkbox" checked={Boolean(item.selected)} onChange={(e) => updateItem(item, { selected: e.target.checked })} />
                      <span className={`pill ${item.kind === 'Task' ? 'pill-info' : 'pill-neutral'}`}>{item.kind}</span>
                      <span className="template-review-title" title={item.title}>{item.title}</span>
                      <label className="template-review-date"><span className="sr-only">Due date</span><input type="date" value={item.dueDate || ''} onChange={(e) => updateItem(item, { dueDate: e.target.value })} /></label>
                      <button className={`duplicate-badge ${item.isDuplicate ? 'duplicate-warning' : 'duplicate-ok'}`} onClick={() => setExpandedWorkTemplateId(expanded ? null : key)}>{item.isDuplicate ? 'Possible match' : 'No likely duplicate'}</button>
                      <button className="row-icon-button" aria-label={expanded ? 'Collapse details' : 'Expand details'} onClick={() => setExpandedWorkTemplateId(expanded ? null : key)}>{expanded ? '▴' : '▾'}</button>
                    </div>
                    {expanded && <div className="template-review-detail">
                      <p><strong>Full title</strong><br />{item.title}</p>
                      <div className="template-review-meta"><span>Template: {item.templateId} v{item.templateVersion}</span><span>Status source: {item.stage || 'Any status'}</span><span>Calculated due date: {item.dueDate || 'Not set'}</span></div>
                      {item.isDuplicate ? <div className="duplicate-comparison"><strong>Duplicate comparison</strong><p>{item.duplicateReason || 'A matching work item already exists.'}</p><label className="toggle-inline"><span>Add anyway</span><input type="checkbox" checked={Boolean(item.allowDuplicate)} onChange={(e) => updateItem(item, { allowDuplicate: e.target.checked, selected: e.target.checked || item.selected })} /></label></div> : <span className="pill pill-success">No likely duplicate detected.</span>}
                    </div>}
                  </div>
                })}
                {visible.length === 0 && <p className="helper-text">No items match this filter.</p>}
              </div>
              <div className="button-row top-gap-small modal-footer"><button className="primary" onClick={() => void addReviewedWorkTemplates()}>Add Selected ({selectedCount})</button><button onClick={() => setWorkTemplatePicker(null)}>Cancel</button></div>
            </>
          })()}
        </ModalShell>
      )}

      {showMergeTagsModal && (
        <ModalShell title="Merge Tags" onClose={() => setShowMergeTagsModal(false)}>
          <p className="helper-text">Copy these tags into your own plain-text template, then upload it in the Documents tab so the program can reuse it later.</p>
          <label className="top-gap-small"><span>Search merge fields</span><input value={mergeTagSearch} onChange={(event) => setMergeTagSearch(event.target.value)} placeholder="Search by tag, meaning, or category" /></label>
          <div className="table-wrap top-gap-small">
            <table className="compact-table">
              <thead>
                <tr>
                  <th>Tag</th>
                  <th>Meaning</th>
                  <th>Source</th>
                  <th>Sample value</th>
                  <th>Copy</th>
                </tr>
              </thead>
              <tbody>
                {templateTags.filter((tag) => `${tag.key} ${tag.label} ${tag.category}`.toLowerCase().includes(mergeTagSearch.toLowerCase())).map((tag) => (
                  <tr key={tag.key}>
                    <td><code>{`{{${tag.key}}}`}</code></td>
                    <td>{tag.label}</td>
                    <td>{tag.category}</td>
                    <td>{sampleMergeTagValue(tag.key)}</td>
                    <td><button onClick={() => navigator.clipboard.writeText(`{{${tag.key}}}`)}>Copy</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="button-row compact-actions modal-footer">
            <button onClick={() => navigator.clipboard.writeText(templateTags.map((tag) => `{{${tag.key}}}`).join('\n'))}>Copy Tag List</button>
            <a className="button-like" href="/api/document-platform/sample-template">Download Sample Template (.docx)</a>
            <button onClick={() => setShowMergeTagsModal(false)}>Close</button>
          </div>
        </ModalShell>
      )}

      {templateDraft && (
        <ModalShell title={templateDraft.id === 0 ? 'Add Checklist Template' : 'Edit Checklist Template'} onClose={() => setTemplateDraft(null)}>
          <div className="form-grid modal-form">
            <label className="full-span"><span>Name</span><input value={templateDraft.name} onChange={(event) => setTemplateDraft({ ...templateDraft, name: event.target.value })} placeholder="Template name" /></label>
            <label>
              <span>Trigger Type</span>
              <select value={templateDraft.triggerType} onChange={(event) => setTemplateDraft({ ...templateDraft, triggerType: event.target.value })}>
                <option value="Stage">Stage</option>
                <option value="IssueTag">Issue Tag</option>
              </select>
            </label>
            {templateDraft.triggerType === 'Stage' ? (
              <label>
                <span>Workflow Status</span>
                <select value={templateDraft.stage || ''} onChange={(event) => setTemplateDraft({ ...templateDraft, stage: event.target.value })}>
                  <option value="">Select status</option>
                  {checklistWorkflowStatuses.map((status) => <option key={status} value={status}>{status}</option>)}
                </select>
              </label>
            ) : (
              <>
                <label>
                  <span>Issue Tag</span>
                  <select value={templateDraft.issueTagName || ''} onChange={(event) => setTemplateDraft({ ...templateDraft, issueTagName: event.target.value })}>
                    <option value="">Select issue tag</option>
                    {allIssueTags.map((tag) => <option key={tag.id} value={tag.name}>{tag.name}</option>)}
                  </select>
                </label>
                <label>
                  <span>Workflow Status (optional filter)</span>
                  <select value={templateDraft.stage || ''} onChange={(event) => setTemplateDraft({ ...templateDraft, stage: event.target.value })}>
                    <option value="">Any status</option>
                    {checklistWorkflowStatuses.map((status) => <option key={status} value={status}>{status}</option>)}
                  </select>
                </label>
              </>
            )}
            <label>
              <span>Track</span>
              <select value={templateDraft.track} onChange={(event) => setTemplateDraft({ ...templateDraft, track: event.target.value })}>
                <option value="Any">Any</option>
                {caseTracks.map((track) => <option key={track} value={track}>{track}</option>)}
              </select>
            </label>
            <label className="toggle-inline"><span>Active</span><input type="checkbox" checked={templateDraft.active} onChange={(event) => setTemplateDraft({ ...templateDraft, active: event.target.checked })} /></label>
          </div>
          <div className="button-row compact-actions modal-footer">
            <button className="primary" onClick={() => void saveChecklistTemplate()}>Save Template</button>
            <button onClick={() => setTemplateDraft(null)}>Cancel</button>
          </div>
        </ModalShell>
      )}

      {templateItemDraft && (
        <ModalShell title={templateItemDraft.id === 0 ? 'Add Template Item' : 'Edit Template Item'} onClose={() => setTemplateItemDraft(null)}>
          <div className="form-grid modal-form">
            <label className="full-span"><span>Task</span><input value={templateItemDraft.task} onChange={(event) => setTemplateItemDraft({ ...templateItemDraft, task: event.target.value })} placeholder="Task text" /></label>
            <label><span>Phase</span><input value={templateItemDraft.phase || ''} onChange={(event) => setTemplateItemDraft({ ...templateItemDraft, phase: event.target.value })} placeholder="Phase label" /></label>
            <label><span>Sort Order</span><input type="number" value={templateItemDraft.sortOrder} onChange={(event) => setTemplateItemDraft({ ...templateItemDraft, sortOrder: Number(event.target.value) || 0 })} /></label>
            <label><span>Due Offset (days from generation)</span><input type="number" value={templateItemDraft.dueOffsetDays ?? ''} onChange={(event) => setTemplateItemDraft({ ...templateItemDraft, dueOffsetDays: event.target.value === '' ? null : Number(event.target.value) })} placeholder="e.g. 14" /></label>
          </div>
          <div className="button-row compact-actions modal-footer">
            <button className="primary" onClick={() => void saveTemplateItem()}>Save Item</button>
            <button onClick={() => setTemplateItemDraft(null)}>Cancel</button>
          </div>
        </ModalShell>
      )}

      {narrativeInputDraft && (
        <ModalShell title="Generate Narrative" onClose={() => setNarrativeInputDraft(null)}>
          <p className="helper-text">Fill in what's not already on file. Filing date, job/tract, acreage, appraised totals, and offer/counteroffer amounts pull automatically from the case, valuation positions, and risk ledger.</p>
          <div className="form-grid modal-form top-gap-small">
            <label className="full-span"><span>Property Description</span><textarea value={narrativeInputDraft.propertyDescription} onChange={(event) => patchNarrativeInputDraft({ propertyDescription: event.target.value })} placeholder="e.g. a rural tract used for pasture and timber production" /></label>
            <label className="full-span"><span>TCE Description (optional)</span><textarea value={narrativeInputDraft.tceDescription} onChange={(event) => patchNarrativeInputDraft({ tceDescription: event.target.value })} placeholder="Leave blank if there is no temporary construction easement" /></label>
            <label className="full-span"><span>Highest and Best Use</span><input value={narrativeInputDraft.highestAndBestUse} onChange={(event) => patchNarrativeInputDraft({ highestAndBestUse: event.target.value })} placeholder="e.g. rural residential / agricultural" /></label>

            <label className="full-span"><strong>ASHC Appraisal (Before / After)</strong></label>
            <label><span>Land Value Before</span><input value={narrativeInputDraft.ourAppraisalLandBefore ?? ''} onChange={(event) => patchNarrativeInputDraft({ ourAppraisalLandBefore: event.target.value === '' ? null : Number(event.target.value.replaceAll(',', '')) })} placeholder="$" /></label>
            <label><span>$/SF Before</span><input value={narrativeInputDraft.ourAppraisalPerSfBefore ?? ''} onChange={(event) => patchNarrativeInputDraft({ ourAppraisalPerSfBefore: event.target.value === '' ? null : Number(event.target.value.replaceAll(',', '')) })} placeholder="$" /></label>
            <label><span>Land Value After</span><input value={narrativeInputDraft.ourAppraisalLandAfter ?? ''} onChange={(event) => patchNarrativeInputDraft({ ourAppraisalLandAfter: event.target.value === '' ? null : Number(event.target.value.replaceAll(',', '')) })} placeholder="$" /></label>
            <label><span>$/SF After</span><input value={narrativeInputDraft.ourAppraisalPerSfAfter ?? ''} onChange={(event) => patchNarrativeInputDraft({ ourAppraisalPerSfAfter: event.target.value === '' ? null : Number(event.target.value.replaceAll(',', '')) })} placeholder="$" /></label>

            <label className="full-span"><strong>Defendant Appraisal (Before / After)</strong></label>
            <label><span>Land Value Before</span><input value={narrativeInputDraft.defendantAppraisalLandBefore ?? ''} onChange={(event) => patchNarrativeInputDraft({ defendantAppraisalLandBefore: event.target.value === '' ? null : Number(event.target.value.replaceAll(',', '')) })} placeholder="$" /></label>
            <label><span>$/SF Before</span><input value={narrativeInputDraft.defendantAppraisalPerSfBefore ?? ''} onChange={(event) => patchNarrativeInputDraft({ defendantAppraisalPerSfBefore: event.target.value === '' ? null : Number(event.target.value.replaceAll(',', '')) })} placeholder="$" /></label>
            <label><span>Land Value After</span><input value={narrativeInputDraft.defendantAppraisalLandAfter ?? ''} onChange={(event) => patchNarrativeInputDraft({ defendantAppraisalLandAfter: event.target.value === '' ? null : Number(event.target.value.replaceAll(',', '')) })} placeholder="$" /></label>
            <label><span>$/SF After</span><input value={narrativeInputDraft.defendantAppraisalPerSfAfter ?? ''} onChange={(event) => patchNarrativeInputDraft({ defendantAppraisalPerSfAfter: event.target.value === '' ? null : Number(event.target.value.replaceAll(',', '')) })} placeholder="$" /></label>

            <label className="full-span"><strong>Offer / Counteroffer / Settlement</strong></label>
            <label><span>ASHC Offer Date</span><input type="date" value={narrativeInputDraft.ashcOfferDate} onChange={(event) => patchNarrativeInputDraft({ ashcOfferDate: event.target.value })} /></label>
            <label><span>Fee Adjustment Amount</span><input value={narrativeInputDraft.feeAdjustmentAmount ?? ''} onChange={(event) => patchNarrativeInputDraft({ feeAdjustmentAmount: event.target.value === '' ? null : Number(event.target.value.replaceAll(',', '')) })} placeholder="$" /></label>
            <label><span>Counteroffer Date</span><input type="date" value={narrativeInputDraft.counterofferDate} onChange={(event) => patchNarrativeInputDraft({ counterofferDate: event.target.value })} /></label>
            <label><span>Settlement Amount</span><input value={narrativeInputDraft.settlementAmount ?? ''} onChange={(event) => patchNarrativeInputDraft({ settlementAmount: event.target.value === '' ? null : Number(event.target.value.replaceAll(',', '')) })} placeholder="$" /></label>
            <label><span>Trial Fee Risk - Low</span><input value={narrativeInputDraft.trialFeeLow ?? ''} onChange={(event) => patchNarrativeInputDraft({ trialFeeLow: event.target.value === '' ? null : Number(event.target.value.replaceAll(',', '')) })} placeholder="$" /></label>
            <label><span>Trial Fee Risk - High</span><input value={narrativeInputDraft.trialFeeHigh ?? ''} onChange={(event) => patchNarrativeInputDraft({ trialFeeHigh: event.target.value === '' ? null : Number(event.target.value.replaceAll(',', '')) })} placeholder="$" /></label>
          </div>
          <div className="button-row compact-actions modal-footer">
            <button className="primary" onClick={() => void generateRiskNarrative()} disabled={narrativeGenerating}>{narrativeGenerating ? 'Generating…' : 'Generate'}</button>
            <button onClick={() => setNarrativeInputDraft(null)}>Cancel</button>
          </div>
        </ModalShell>
      )}

      {page === 'dashboard' && (
        <main className="page">
          <div className="dash-hd">
            <h2>{dashboardGreeting}</h2>
            <span className="dash-date">{dashboardDateLine}</span>
            {dashboardHeadline.activeCaseCount == null ? (
              <span className="muted">Loading case status…</span>
            ) : (
              <span className="muted">
                {dashboardHeadline.activeCaseCount} active case{dashboardHeadline.activeCaseCount === 1 ? '' : 's'} ·{' '}
                <strong className={dashboardHeadline.actionsNeededNow > 0 ? 'ui-cell-danger' : undefined}>{dashboardHeadline.actionsNeededNow} need action now</strong> ·{' '}
                {dashboardHeadline.dueThisWeekCount} due this week
              </span>
            )}
          </div>

          {attorneyDashboardError && <ErrorState message={attorneyDashboardError} onRetry={() => void loadAttorneyDashboard(attorneyDashboardFilters)} />}

          {attorneyDashboardLoading && !attorneyDashboard ? (
            <LoadingSkeleton rows={6} />
          ) : attorneyDashboard && (
            <>
              <div className="ui-tiles" style={{ marginBottom: '1rem' }}>
                {PRIORITY_TILES.map((tile) => (
                  <MetricTile
                    key={tile.level}
                    label={tile.label}
                    value={priorityQueueCounts[tile.level] ?? 0}
                    tone={tile.tone}
                    active={activeQueueTiles.has(tile.level)}
                    onClick={() => toggleQueueTile(tile.level)}
                  />
                ))}
                <MetricTile label="Awaiting triage" value={attorneyDashboard.triageCaseCount} onClick={() => goToTriageQueue()} />
              </div>

              <div className="button-row compact-actions">
                <button onClick={() => setDashboardFiltersOpen(true)}>
                  Filters{Object.values(attorneyDashboardFilters).filter((value) => value !== undefined && value !== '' && value !== null).length > 0 ? ` (${Object.values(attorneyDashboardFilters).filter((value) => value !== undefined && value !== '' && value !== null).length})` : ''}
                </button>
              </div>

              {dashboardFiltersOpen && (
                <>
                  <div className="slideover-backdrop" onClick={() => setDashboardFiltersOpen(false)} />
                  <aside className="filter-slideover" role="dialog" aria-label="Dashboard filters">
                    <div className="button-row split-row">
                      <strong>Filters</strong>
                      <button onClick={() => setDashboardFiltersOpen(false)}>Close</button>
                    </div>
                    <ActionQueueFilters
                      filters={attorneyDashboardFilters}
                      counties={arkansasCounties}
                      projects={Array.from(new Set(attorneyDashboard.projectWatch.map((p) => p.projectName)))}
                      onChange={setAttorneyDashboardFilters}
                    />
                  </aside>
                </>
              )}

              <div className="dash-cols">
                <div className="ui-table-panel">
                  <div className="panel-hd">
                    <h3>Action Queue</h3>
                    <span className="count">
                      {filteredActionQueue.length} item{filteredActionQueue.length === 1 ? '' : 's'}
                      {activeQueueTiles.size > 0 && activeQueueTiles.size < PRIORITY_TILES.length && ` · filtered: ${PRIORITY_TILES.filter((tile) => activeQueueTiles.has(tile.level)).map((tile) => tile.label).join(', ')}`}
                    </span>
                  </div>
                  <div className="table-wrap">
                    <table className="ui-table">
                      <thead>
                        <tr>
                          <th style={{ width: 28 }}></th>
                          <th>Case</th>
                          <th>Why it's here</th>
                          <th style={{ width: 130 }}>Review by</th>
                          <th style={{ width: 260 }}></th>
                        </tr>
                      </thead>
                      <tbody>
                        {filteredActionQueue.length === 0 ? (
                          <UiEmptyState colSpan={5} title="Nothing needs attorney judgment right now" hint="Cases needing a decision, action, review, escalation, or trial preparation will appear here." />
                        ) : filteredActionQueue.map((item) => (
                          <ActionQueueRow
                            key={item.caseId}
                            item={item}
                            handlers={actionQueueHandlers}
                            selected={selectedActionQueueIds.includes(item.caseId)}
                            onToggleSelect={(caseId) => setSelectedActionQueueIds((prev) => (prev.includes(caseId) ? prev.filter((id) => id !== caseId) : [...prev, caseId]))}
                            county={dashboardCasesById.get(item.caseId)?.county}
                          />
                        ))}
                      </tbody>
                    </table>
                  </div>
                  {filteredActionQueue.length > 0 && (
                    <div className="ui-table-footer">
                      <label className="toggle-inline">
                        <span>Select all</span>
                        <input
                          type="checkbox"
                          checked={selectedActionQueueIds.length > 0 && selectedActionQueueIds.length === filteredActionQueue.length}
                          onChange={(event) => setSelectedActionQueueIds(event.target.checked ? filteredActionQueue.map((item) => item.caseId) : [])}
                        />
                      </label>
                      {selectedActionQueueIds.length > 0 && (
                        <span className="helper-text">{selectedActionQueueIds.length} selected</span>
                      )}
                      {selectedActionQueueIds.length > 0 && (
                        <Btn size="sm" onClick={() => { applyBulkDeferPreset('7'); setBulkDeferOpen(true) }}>Defer selected…</Btn>
                      )}
                      {activeQueueTiles.size > 0 && activeQueueTiles.size < PRIORITY_TILES.length && (
                        <span className="ui-cell-faint" style={{ marginLeft: 'auto', fontSize: '.78rem' }}>
                          {PRIORITY_TILES.filter((tile) => !activeQueueTiles.has(tile.level)).map((tile) => tile.label).join(' & ')} hidden — click tiles above to include
                        </span>
                      )}
                    </div>
                  )}
                  {bulkDeferOpen && selectedActionQueueIds.length > 0 && (
                    <form
                      className="inline-quick-form"
                      style={{ borderTop: '1px solid var(--border)', padding: '0.75rem 0.9rem' }}
                      onSubmit={(event) => { event.preventDefault(); void submitBulkDefer() }}
                    >
                      <label>
                        Defer interval
                        <select value={bulkDeferPreset} onChange={(event) => applyBulkDeferPreset(event.target.value as '7' | '14' | '30' | 'custom')}>
                          <option value="7">7 days (default)</option>
                          <option value="14">14 days</option>
                          <option value="30">30 days</option>
                          <option value="custom">Custom date</option>
                        </select>
                      </label>
                      <label>
                        Future review date (applied to all selected)
                        <input type="date" value={bulkDeferDate} onChange={(event) => setBulkDeferDate(event.target.value)} required />
                      </label>
                      <Btn size="sm" variant="primary" type="submit">Defer Selected</Btn>
                      <Btn size="sm" type="button" onClick={() => setBulkDeferOpen(false)}>Cancel</Btn>
                    </form>
                  )}
                </div>

                {/* The seven former always-visible side panels, folded into one tabbed panel so
                    the working surface is the queue plus exactly one context view at a time. */}
                <Panel title="Case Insight">
                  <div className="segmented-tabs compact-segments">
                    {([
                      { key: 'docket', label: 'Docket' },
                      { key: 'discovery', label: 'Discovery' },
                      { key: 'momentum', label: 'Momentum' },
                      { key: 'pipeline', label: 'Pipeline' },
                      { key: 'trial', label: 'Trials' },
                      { key: 'projects', label: 'Projects' },
                    ] as const).map((tab) => (
                      <button key={tab.key} className={dashboardPanelTab === tab.key ? 'segment active' : 'segment'} onClick={() => setDashboardPanelTab(tab.key)}>
                        {tab.label}
                      </button>
                    ))}
                  </div>

                  <div className="top-gap-small">
                    {dashboardPanelTab === 'discovery' && (
                      <DiscoveryControlPanel summary={attorneyDashboard.discoveryControl} onOpenCase={(id) => openCase(id, 'discovery')} />
                    )}

                    {dashboardPanelTab === 'momentum' && (
                      <MomentumReviewPanel entries={attorneyDashboard.momentumReview} onOpenCase={(id) => openCase(id, 'overview')} />
                    )}

                    {dashboardPanelTab === 'pipeline' && (
                      <FilingPipelinePanel pipeline={attorneyDashboard.filingPipeline} onOpenCase={(id) => openCase(id, 'overview')} onHandoff={openHandoffDialog} onNote={(id) => void pipelineNote(id)} onHolder={(id) => void pipelineHolder(id)} onReview={(id) => void pipelineReview(id)} onAdvance={(id) => void advancePipelineCase(id)} />
                    )}

                    {dashboardPanelTab === 'trial' && (
                      attorneyDashboard.trialWatch.length === 0 ? (
                        <EmptyState title="No trial-track cases" description="Cases on the trial track will appear here." />
                      ) : (
                        <TrialWatchTable entries={attorneyDashboard.trialWatch} onOpenCase={(id) => openCase(id, 'trialNotebook')} />
                      )
                    )}

                    {dashboardPanelTab === 'projects' && (
                      !attorneyDashboard.projectWatch.some((p) => p.sharedIssue) ? (
                        <EmptyState title="No project-wide issues" description="Projects with a shared issue across tracts will appear here." />
                      ) : (
                        <div className="project-watch-list">
                          {attorneyDashboard.projectWatch.filter((p) => p.sharedIssue).map((p) => (
                            <ProjectWatchRowCard key={p.projectName} project={p} />
                          ))}
                        </div>
                      )
                    )}

                    {dashboardPanelTab === 'docket' && (
                      <>
                        <div className="kv">
                          {([
                            ['preFiling', 'Pre-filing matters', attorneyDashboard.docketSummary.preFilingMatters, ''],
                            ['filed', 'Filed matters', attorneyDashboard.docketSummary.filedMatters, ''],
                            ['trial', 'Trial-track matters', attorneyDashboard.docketSummary.trialTrackMatters, ''],
                            ['waiting', 'Waiting on others', attorneyDashboard.docketSummary.waitingAppropriately, ''],
                            ['desk', "On attorney's desk", attorneyDashboard.docketSummary.onAttorneysDesk, 'warn'],
                            ['missingReview', 'Missing next review date', attorneyDashboard.docketSummary.missingNextReviewDate, 'danger'],
                          ] as const).map(([key, label, value, tone]) => (
                            <button
                              key={key}
                              className={`kv-row${docketMetricFilter === key ? ' kv-row-active' : ''}`}
                              onClick={() => setDocketMetricFilter(docketMetricFilter === key ? null : key)}
                            >
                              <span>{label}</span>
                              <span className={`v${tone && value > 0 ? ` ui-cell-${tone}` : ''}`}>{value}</span>
                            </button>
                          ))}
                        </div>
                        {docketMetricFilter && <div className="docket-filtered-list top-gap-small"><div className="button-row compact-actions"><strong>{docketCases.length} matching case{docketCases.length === 1 ? '' : 's'}</strong><button onClick={() => setDocketMetricFilter(null)}>Clear metric filter</button></div>{docketCases.length === 0 ? <p className="helper-text">No cases match this metric.</p> : <div className="table-wrap"><table className="compact-table"><thead><tr><th>Case</th><th>Status</th><th>Holder</th><th>Next review</th><th>Next action</th><th></th></tr></thead><tbody>{docketCases.map((c) => <tr key={c.id}><td>{c.caseName || c.caseNumber || ('Case ' + c.id)}</td><td>{c.caseStatus || 'Pipeline'}</td><td>{c.currentHolder || 'Not assigned'}</td><td>{displayDate(c.nextReviewDate)}</td><td>{c.nextAction || 'Not set'}</td><td><button onClick={() => openCase(c.id, 'overview')}>Open Case</button></td></tr>)}</tbody></table></div>}</div>}
                      </>
                    )}
                  </div>
                </Panel>
              </div>

              <div className="ui-table-panel" style={{ marginTop: '1rem' }}>
                <div className="panel-hd">
                  <h3>Due in the next 7 days</h3>
                  <span className="count">{dashboardDueThisWeekItems.length} item{dashboardDueThisWeekItems.length === 1 ? '' : 's'}</span>
                  <Btn size="sm" variant="ghost" onClick={() => setPage('queues')}>Full work queue ▸</Btn>
                </div>
                <div className="table-wrap">
                  <table className="ui-table">
                    <thead>
                      <tr>
                        <th style={{ width: 90 }}>Type</th>
                        <th>Item</th>
                        <th>Case</th>
                        <th style={{ width: 130 }}>Due</th>
                        <th style={{ width: 170 }}></th>
                      </tr>
                    </thead>
                    <tbody>
                      {dashboardDueThisWeekItems.length === 0 ? (
                        <UiEmptyState colSpan={5} title="Nothing due in the next 7 days" hint="Deadlines, tasks, discovery, service, and hearings due soon will appear here." />
                      ) : dashboardDueThisWeekItems.slice(0, 10).map((item) => (
                        <tr key={item.key}>
                          <td><TypeChip kind={item.type === 'hearing' ? 'event' : item.type} /></td>
                          <td>{item.title}</td>
                          <td className="ui-sub">{item.caseName}</td>
                          <td className={`ui-data${item.dueDate && item.dueDate <= new Date().toISOString().slice(0, 10) ? ' ui-cell-danger' : ''}`}>{item.dueDate ? displayDate(item.dueDate) : '—'}</td>
                          <td>
                            <div className="ui-row-actions">
                              {item.type === 'task' && <Btn size="sm" onClick={() => { const source = item.source as ChecklistItem | undefined ?? queueChecklist.find((candidate) => item.key === `task-${candidate.id}`); if (source) void persistChecklist({ ...source, status: 'Done' }, 'Task marked done.', false) }}>Mark done</Btn>}
                              {item.type === 'deadline' && <Btn size="sm" onClick={() => { const source = item.source as DeadlineItem | undefined ?? queueDeadlines.find((candidate) => item.key === `deadline-${candidate.id}`); if (source) void persistDeadline({ ...source, status: 'Done' }, 'Deadline marked done.', false) }}>Complete</Btn>}
                              {item.type === 'service' && <Btn size="sm" onClick={() => void markGlobalServicePerfected(item.caseId)}>Perfect Service</Btn>}
                              {item.type === 'discovery' && <Btn size="sm" onClick={() => { const source = item.source as DiscoveryItem | undefined ?? queueDiscovery.find((candidate) => item.key === `discovery-${candidate.id}`); if (source) void recordDiscoveryResponse(source) }}>Record Response</Btn>}
                              <Btn size="sm" variant="ghost" onClick={() => openCase(item.caseId, item.tab)}>Open ▸</Btn>
                            </div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                {dashboardDueThisWeekItems.length > 10 && (
                  <p className="footnote" style={{ padding: '0.5rem 0.9rem' }}>and {dashboardDueThisWeekItems.length - 10} more…</p>
                )}
              </div>
            </>
          )}
        </main>
      )}

      {handoffTarget && (
        <PipelineHandoffDialog caseName={handoffTarget.caseName} onClose={() => setHandoffTarget(null)} onSubmit={submitHandoff} />
      )}
      {caseRecordDecisionOpen && (
        <RecordDecisionDialog caseName={selectedCase.caseName || 'this case'} onClose={() => setCaseRecordDecisionOpen(false)} onSubmit={submitCaseRecordDecision} />
      )}
      {triageWizardOpen && selectedCase.status === 'Triage' && (
        <TriageWizard
          caseData={{
            caseName: selectedCase.caseName || '',
            caseNumber: selectedCase.caseNumber || '',
            jobNumber: selectedCase.jobNumber || '',
            tract: selectedCase.tract || '',
            county: selectedCase.county || '',
            caseStatus: selectedCase.caseStatus && selectedCase.caseStatus !== 'Triage' ? selectedCase.caseStatus : '',
            filingDate: selectedCase.filingDate || '',
            servicePerfected: selectedCase.servicePerfected,
            servicePerfectedDate: selectedCase.servicePerfectedDate || '',
            trialDate: selectedCase.trialDate || '',
            closedDate: selectedCase.closedDate || '',
          }}
          counties={arkansasCounties}
          workflowStatuses={consolidatedCaseStatuses.filter((status) => status !== 'Pipeline' && status !== 'Triage')}
          onSaveStep={(patch) => persistCasePatch(patch, 'Triage progress saved.')}
          onActivate={activateTriageCase}
          onClose={() => setTriageWizardOpen(false)}
        />
      )}

      {page === 'cases' && (casesView === 'list' ? renderCaseListPage() : renderCaseWorkspace())}

      {page === 'reports' && (
        <main className="page">
          <section className="hero-panel">
            <div><p className="eyebrow dark">Reports</p><h2>Open Case Reports</h2><p className="subtle-text">Build a read-only case list from the same consolidated status data used throughout the application.</p></div>
            <div className="button-row compact-actions"><button onClick={exportReportCsv}>Export CSV</button><button className="primary" onClick={() => void exportReportExcel()}>Export Excel</button></div>
          </section>
          <div className="report-builder-grid">
            <CollapsiblePanel title="Filters">
              <div className="form-grid">
                <label><span>Case status</span><select value={reportStatusFilter} onChange={(event) => setReportStatusFilter(event.target.value)}><option value="">All open statuses</option>{consolidatedCaseStatuses.filter((status) => status !== 'Triage').map((status) => <option key={status}>{status}</option>)}<option value="__closed">Closed / resolved</option></select></label>
                <label><span>County</span><select value={reportCountyFilter} onChange={(event) => setReportCountyFilter(event.target.value)}><option value="">All counties</option>{arkansasCounties.map((county) => <option key={county}>{county}</option>)}</select></label>
                <label><span>Search cases</span><input value={reportSearch} onChange={(event) => setReportSearch(event.target.value)} placeholder="Name, number, job, tract..." /></label>
                <label><span>Date preset</span><select value={reportPreset} onChange={(event) => applyReportPreset(event.target.value)}><option value="">Custom range</option><option value="30">Last 30 days</option><option value="90">Last 90 days</option><option value="6m">Last 6 months</option><option value="12m">Last 12 months</option><option value="thisYear">This calendar year</option><option value="previousYear">Previous calendar year</option></select></label>
                {reportPreset === '' && <>
                  <label><span>Date opened from</span><input type="date" value={reportOpenedFrom} onChange={(event) => setReportOpenedFrom(event.target.value)} /></label>
                  <label><span>Date opened to</span><input type="date" value={reportOpenedTo} onChange={(event) => setReportOpenedTo(event.target.value)} /></label>
                </>}
              </div>
              <div className="button-row compact-actions top-gap-small"><button onClick={() => { setReportStatusFilter(''); setReportCountyFilter(''); setReportSearch(''); setReportPreset(''); setReportOpenedFrom(''); setReportOpenedTo('') }}>Reset Filters</button><span className="helper-text">Date boundaries are inclusive; presets populate the range automatically.</span></div>
            </CollapsiblePanel>
            <CollapsiblePanel title="Columns and layout">
              <div className="report-column-picker">{reportColumnOptions.map((option) => <label className="toggle-inline" key={option.key}><span>{option.label}</span><input type="checkbox" checked={reportColumns.includes(option.key)} onChange={(event) => setReportColumns((current) => event.target.checked ? [...current, option.key] : current.filter((column) => column !== option.key))} /></label>)}</div>
              <div className="form-grid top-gap-small"><label><span>Sort by</span><select value={reportSortColumn} onChange={(event) => setReportSortColumn(event.target.value as ReportColumnKey)}>{reportColumns.map((column) => <option key={column} value={column}>{reportColumnOptions.find((option) => option.key === column)?.label}</option>)}</select></label><label><span>Direction</span><select value={reportSortDirection} onChange={(event) => setReportSortDirection(event.target.value as 'asc' | 'desc')}><option value="asc">Ascending</option><option value="desc">Descending</option></select></label></div>
            </CollapsiblePanel>
          </div>
          <CollapsiblePanel title="Case duration & age" defaultOpen={false}>
            <div className="metric-tile-row">
              <div className="metric-tile"><span>Avg. closed duration</span><strong>{reportMetrics.averageDuration == null ? '—' : `${reportMetrics.averageDuration} days`}</strong></div>
              <div className="metric-tile"><span>Avg. open age</span><strong>{reportMetrics.averageAge == null ? '—' : `${reportMetrics.averageAge} days`}</strong></div>
              <div className="metric-tile"><span>Median closed duration</span><strong>{reportMetrics.medianDuration == null ? '—' : `${reportMetrics.medianDuration} days`}</strong></div>
              <div className="metric-tile"><span>Shortest closed duration</span><strong>{reportMetrics.shortestDuration == null ? '—' : `${reportMetrics.shortestDuration} days`}</strong></div>
              <div className="metric-tile"><span>Longest closed duration</span><strong>{reportMetrics.longestDuration == null ? '—' : `${reportMetrics.longestDuration} days`}</strong></div>
            </div>
            {reportMetrics.closed > 0 && <p className="helper-text top-gap-small">Duration metrics use {reportMetrics.closed - reportMetrics.missingDates} of {reportMetrics.closed} closed cases. Cases missing Date Opened or Date Closed are excluded.</p>}
            {reportMetrics.open > 0 && <p className="helper-text">Open-case age bands: &lt;90 days {reportMetrics.ageBands.under90} · 90–179 {reportMetrics.ageBands.days90to179} · 180–364 {reportMetrics.ageBands.days180to364} · 1–2 years {reportMetrics.ageBands.year1to2} · 2–3 years {reportMetrics.ageBands.year2to3} · &gt;3 years {reportMetrics.ageBands.over3}.</p>}
          </CollapsiblePanel>
          <Panel title="Preview" headerAction={<span className="pill pill-neutral">{reportRows.length} matching case{reportRows.length === 1 ? '' : 's'}</span>}>
            {reportColumns.length === 0 ? <p>Select at least one column to preview the report.</p> : reportRows.length === 0 ? <p>No cases match the current filters.</p> : <div className="table-wrap"><table className="compact-table"><thead><tr>{reportColumns.map((column) => <th key={column}>{reportColumnOptions.find((option) => option.key === column)?.label}</th>)}<th>Open</th></tr></thead><tbody>{reportRows.map((record) => <tr key={record.id}>{reportColumns.map((column) => <td key={column}>{reportCellValue(record, column) || '—'}</td>)}<td><button onClick={() => openCase(record.id, 'overview')}>Open Case</button></td></tr>)}</tbody></table></div>}
          </Panel>
        </main>
      )}

      {page === 'queues' && renderWorkQueuePage()}

      {page === 'settings' && (
        <main className="page">
          <section className="hero-panel">
            <div>
              <p className="eyebrow dark">Settings</p>
              <h2>Settings, Import, and Diagnostics</h2>
              <p className="subtle-text">Appearance, local storage paths, CSV import, diagnostics, and IT notes all live here so the top navigation stays focused on daily work.</p>
            </div>
          </section>

          <div className="settings-layout">
            <aside className="settings-nav" aria-label="Settings categories">
              {settingsCategories.map((category) => (
                <div className="settings-nav-group" key={category.label}>
                  <p>{category.label}</p>
                  {category.sections.map((key) => {
                    const section = settingsSections.find((item) => item.key === key)!
                    return <button key={key} className={key === settingsSection ? 'active' : ''} onClick={() => setSettingsSection(key)}>{section.label}</button>
                  })}
                </div>
              ))}
            </aside>
            <section className="settings-content">

          {settingsSection === 'appearance' && (
            <Panel title="Appearance">
              <div className="appearance-grid">
                <label>
                  <span>Theme</span>
                  <select value={theme} onChange={(event) => setTheme(event.target.value as ThemeMode)}>
                    <option value="light">Light</option>
                    <option value="dark">Dark</option>
                    <option value="system">Use System Setting</option>
                  </select>
                </label>
              </div>
              <p className="helper-text top-gap-small">Theme preference is stored locally in this browser.</p>
            </Panel>
          )}

          {settingsSection === 'import' && (
            <Panel title="Import Cases">
              <form className="import-form" onSubmit={(event) => void importCases(event, '/api/import/cases-xlsx')}>
                <div className="file-picker">
                  <span>Excel case list (.xlsx / .xlsm)</span>
                  <div className="file-picker-row">
                    <label className="button-like">
                      <input
                        className="hidden-file-input"
                        type="file"
                        name="file"
                        accept=".xlsx,.xlsm"
                        required
                        onChange={(event) => setImportExcelFileName(event.currentTarget.files?.[0]?.name || 'No file selected.')}
                      />
                      Browse Excel…
                    </label>
                    <input readOnly value={importExcelFileName} aria-label="Selected Excel file" />
                  </div>
                </div>
                <button className="primary" type="submit">Import Excel</button>
              </form>
              <p className="helper-text">Reads the <strong>Open</strong>, <strong>Closed</strong>, and <strong>Discovery</strong> sheets of the condemnation case list workbook. Existing cases are matched by case number (or job + tract) and updated rather than duplicated. Imported historical cases remain in Triage so you can assign the correct consolidated Workflow Status before activation.</p>
              <form className="import-form top-gap" onSubmit={(event) => void importCases(event, '/api/import/cases-csv')}>
                <div className="file-picker">
                  <span>CSV file</span>
                  <div className="file-picker-row">
                    <label className="button-like">
                      <input
                        className="hidden-file-input"
                        type="file"
                        name="file"
                        accept=".csv"
                        required
                        onChange={(event) => setImportFileName(event.currentTarget.files?.[0]?.name || 'No file selected.')}
                      />
                      Browse CSV…
                    </label>
                    <input readOnly value={importFileName} aria-label="Selected CSV file" />
                  </div>
                </div>
                <button className="primary" type="submit">Import CSV</button>
              </form>
              <p>Sample CSV file: <code>import_samples/sample_cases.csv</code>. Blank dates stay blank, 1900-01-01 is treated as blank, and money values can include commas or dollar signs.</p>
              {importResult && (
                <div className="summary-card">
                  <strong>Import Summary</strong>
                  <p>Rows read: {importResult.rowsRead} | Created: {importResult.created} | Updated: {importResult.updated} | Skipped: {importResult.skipped}</p>
                  {(importResult.info?.length ?? 0) > 0 && <ul className="plain-list">{importResult.info!.map((line) => <li key={line}>{line}</li>)}</ul>}
                  {importResult.errors.length > 0 && <ul className="plain-list">{importResult.errors.map((error) => <li key={error}>{error}</li>)}</ul>}
                </div>
              )}
            </Panel>
          )}

          {settingsSection === 'diagnostics' && (
            <Panel title="Diagnostics">
              {diagnostics ? (
                <div className="diagnostics-grid">
                  <StatCard label="App / Version" value={`${diagnostics.appName} | ${diagnostics.version}`} />
                  <StatCard label="Database Provider" value={diagnostics.databaseProvider} />
                  <StatCard label="Database Writable" value={diagnostics.databaseWritable ? 'Yes' : 'No'} tone={diagnostics.databaseWritable ? 'ok' : 'warn'} />
                  <StatCard label="Write Safety" value={diagnostics.writeSafetyOk ? 'Safe' : 'Read Only'} tone={diagnostics.writeSafetyOk ? 'ok' : 'warn'} />
                  <StatCard label="Counts" value={`Cases ${diagnostics.caseCount} | Deadlines ${diagnostics.deadlineCount} | Checklist ${diagnostics.checklistCount} | Discovery ${diagnostics.discoveryCount}`} />
                  <PathField label="Database path" value={diagnostics.databasePath} />
                  <label><span>Database architecture note</span><textarea readOnly value={diagnostics.databaseArchitectureNote} /></label>
                  <label><span>Write-safety details</span><textarea readOnly value={diagnostics.writeSafetyMessage} /></label>
                  <label><span>Last import result</span><input readOnly value={diagnostics.lastImportResult || 'None'} /></label>
                  <label><span>Last document generation result</span><input readOnly value={diagnostics.lastDocumentGenerationResult || 'None'} /></label>
                  <PathField label="Latest log path" value={diagnostics.latestLogPath || 'Not available'} />
                </div>
              ) : <p>Diagnostics are loading.</p>}
            </Panel>
          )}

          {settingsSection === 'storage' && (
            <Panel title="Local Storage / Paths">
              <div className="settings-subpanel">
                <div className="panel-header"><div><h3>Case Status Migration Review {statusMappingReviewCases.length > 0 && <span className="pill pill-warn">{statusMappingReviewCases.length} unresolved</span>}</h3><p className="helper-text">Administrative review for cases whose legacy stage, track, or status could not be mapped confidently.</p></div><button onClick={() => void loadStatusMappingReview()}>Refresh Review</button></div>
                {statusMappingReviewCases.length > 0 ? <div className="table-wrap top-gap-small"><table className="compact-table"><thead><tr><th>Case</th><th>Legacy values</th><th>Projected status</th><th>Action</th></tr></thead><tbody>{statusMappingReviewCases.map((item) => <tr key={item.id}><td>{item.caseName || item.caseNumber || ('Case ' + item.id)}</td><td>{[item.status, item.stage, item.track].filter(Boolean).join(' · ')}</td><td>{item.caseStatus || 'Review needed'}</td><td><button onClick={() => { setSelectedCaseId(item.id); setCasesView('workspace'); setPage('cases') }}>Open Case</button></td></tr>)}</tbody></table></div> : <p className="helper-text top-gap-small">No ambiguous mappings loaded. Select Refresh Review to check.</p>}
              </div>
              <p className="helper-text">These local folder paths are read-only in the browser build. Browse selection is offered where the browser can safely pick a file, such as CSV import.</p>
              <div className="readonly-grid top-gap-small">
                {diagnostics && Object.entries(diagnostics.folders).map(([name, value]) => (
                  <PathField key={name} label={name} value={value} />
                ))}
              </div>
              <div className="settings-subpanel top-gap-small">
                <h3>Sample Data</h3>
                <p className="helper-text">Only the clearly fictional seeded case is eligible. Existing databases are never reseeded automatically.</p>
                <button onClick={() => void deleteSampleData()} disabled={!diagnostics?.sampleDataExists}>Delete Sample Data</button>
                {!diagnostics?.sampleDataExists && <span className="helper-text inline-help">No recognized sample data is present.</span>}
              </div>
              <div className="settings-subpanel top-gap-small">
                <h3>Advanced Reset</h3>
                <p className="helper-text">Creates and verifies a safety backup, replaces the SQLite database, runs migrations, and reseeds one fictional sample case. This cannot be undone except by restoring the safety backup.</p>
                <button className="danger-button" onClick={() => void resetEntireDatabase()}>Reset Entire Database</button>
              </div>
            </Panel>
          )}

          {settingsSection === 'documentDefaults' && (
            <Panel title="Document Defaults">
              <p className="helper-text">These values fill the signature block and memo routing fields on generated court documents so you don't retype them every time.</p>
              <div className="form-grid top-gap-small">
                <label><span>Attorney Name</span><input value={orgDefaults.attorneyName} onChange={(event) => setOrgDefaults({ ...orgDefaults, attorneyName: event.target.value })} placeholder="Attorney name" /></label>
                <label><span>Bar Number</span><input value={orgDefaults.barNumber} onChange={(event) => setOrgDefaults({ ...orgDefaults, barNumber: event.target.value })} placeholder="Bar number" /></label>
                <label><span>Phone</span><input value={orgDefaults.phone} onChange={(event) => setOrgDefaults({ ...orgDefaults, phone: event.target.value })} placeholder="Phone" /></label>
                <label><span>Email</span><input value={orgDefaults.email} onChange={(event) => setOrgDefaults({ ...orgDefaults, email: event.target.value })} placeholder="Email" /></label>
                <label><span>Address Line 1</span><input value={orgDefaults.addressLine1} onChange={(event) => setOrgDefaults({ ...orgDefaults, addressLine1: event.target.value })} placeholder="Address line 1" /></label>
                <label><span>Address Line 2</span><input value={orgDefaults.addressLine2} onChange={(event) => setOrgDefaults({ ...orgDefaults, addressLine2: event.target.value })} placeholder="Address line 2" /></label>
                <label><span>Division Head Name</span><input value={orgDefaults.divisionHeadName} onChange={(event) => setOrgDefaults({ ...orgDefaults, divisionHeadName: event.target.value })} placeholder="Right of Way division head" /></label>
                <label><span>ROW Section Head Name</span><input value={orgDefaults.rowSectionHeadName} onChange={(event) => setOrgDefaults({ ...orgDefaults, rowSectionHeadName: event.target.value })} placeholder="Administrative section head" /></label>
                <label><span>Chief Legal Counsel Name</span><input value={orgDefaults.chiefLegalCounselName} onChange={(event) => setOrgDefaults({ ...orgDefaults, chiefLegalCounselName: event.target.value })} placeholder="Chief Legal Counsel" /></label>
              </div>
              <div className="button-row compact-actions top-gap-small">
                <button className="primary" onClick={() => void saveOrgDefaults()}>Save Document Defaults</button>
              </div>
            </Panel>
          )}

          {settingsSection === 'referenceLibrary' && (
            <Panel title="Reference Library">
              <div className="button-row compact-actions top-gap-small">
                <button className="primary" onClick={() => { setReferenceEditKey(''); setReferenceEditDraft({ key: '', title: '', description: '', text: '' }) }}>Add Reference Document</button>
              </div>
              {referenceEditDraft && (
                <div className="panel reference-doc top-gap-small">
                  <div className="panel-body">
                    <div className="form-grid two-column">
                      <label><span>Key</span><input disabled={Boolean(referenceEditKey)} value={referenceEditDraft.key} onChange={(event) => setReferenceEditDraft({ ...referenceEditDraft, key: event.target.value })} placeholder="e.g. jury_instructions" /></label>
                      <label><span>Title</span><input value={referenceEditDraft.title} onChange={(event) => setReferenceEditDraft({ ...referenceEditDraft, title: event.target.value })} /></label>
                      <label className="full-span"><span>Description</span><input value={referenceEditDraft.description} onChange={(event) => setReferenceEditDraft({ ...referenceEditDraft, description: event.target.value })} /></label>
                      <label className="full-span"><span>Text</span><textarea className="document-preview-textarea" value={referenceEditDraft.text} onChange={(event) => setReferenceEditDraft({ ...referenceEditDraft, text: event.target.value })} /></label>
                    </div>
                    <div className="button-row compact-actions top-gap-small">
                      <button className="primary" onClick={() => void saveReferenceDocument()}>Save</button>
                      <button onClick={() => { setReferenceEditKey(null); setReferenceEditDraft(null) }}>Cancel</button>
                    </div>
                  </div>
                </div>
              )}
              <p className="helper-text">Reference material to copy from when drafting — prior-case documents, jury instructions, or anything else worth keeping on hand. Nothing here is auto-generated, and nothing changes per case. Starts empty; add your own documents below.</p>
              {referenceLibrary.length === 0 ? (
                <p className="top-gap-small">No reference documents yet. Use "Add Reference Document" above to add one.</p>
              ) : (
                <div className="stacked-panels top-gap-small">
                  {referenceLibrary.map((doc) => (
                    <div key={doc.key} className="panel reference-doc">
                      <div className="panel-header">
                        <h3>{doc.title}</h3>
                      </div>
                      <div className="panel-body">
                        <p className="helper-text">{doc.description}</p>
                        <div className="button-row compact-actions">
                          <button onClick={() => setExpandedRefKey(expandedRefKey === doc.key ? null : doc.key)}>
                            {expandedRefKey === doc.key ? 'Hide' : 'View'}
                          </button>
                          <button onClick={() => navigator.clipboard.writeText(doc.text)}>Copy to Clipboard</button>
                          <button onClick={() => { setReferenceEditKey(doc.key); setReferenceEditDraft({ ...doc }) }}>Edit</button>
                          <button onClick={() => void deleteReferenceDocument(doc.key)}>Remove</button>
                        </div>
                        {expandedRefKey === doc.key && (
                          <textarea className="document-preview-textarea top-gap-small" readOnly value={doc.text} />
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </Panel>
          )}

          {settingsSection === 'checklistTemplates' && (
            <Panel title="Checklist Templates">
              <p className="helper-text">These templates drive the tasks and deadlines a case can pull in. Nothing is generated automatically on save anymore — use this to (re)apply templates to a specific case, e.g. after editing a template or fixing a case's stage.</p>
              <div className="compact-info-grid top-gap-small">
                <label>
                  <span>Case</span>
                  <select value={templateRegenCaseId} onChange={(event) => setTemplateRegenCaseId(Number(event.target.value))}>
                    <option value={0}>Select a case...</option>
                    {allCases.map((c) => <option key={c.id} value={c.id}>{c.caseName || c.caseNumber || `Case ${c.id}`}</option>)}
                  </select>
                </label>
              </div>
              <div className="button-row compact-actions top-gap-small">
                <button onClick={() => void regenerateTemplatesForPickedCase()} disabled={!templateRegenCaseId || templateRegenBusy}>Regenerate Checklist &amp; Deadlines From Templates</button>
              </div>
              <p className="helper-text top-gap-small">Add Template below to create or edit the templates themselves.</p>
              <div className="button-row compact-actions top-gap-small">
                <button className="primary" onClick={startNewChecklistTemplate}>Add Template</button>
              </div>
              <div className="stacked-panels top-gap-small">
                {checklistTemplates.length === 0 ? <p>No checklist templates yet.</p> : checklistTemplates.map((template) => (
                  <div key={template.id} className="panel reference-doc">
                    <div className="panel-header">
                      <h3>{template.name}{!template.active && <span className="pill pill-neutral inline-pill">Inactive</span>}</h3>
                    </div>
                    <div className="panel-body">
                      <p className="helper-text">
                        {template.triggerType === 'Stage' ? `Workflow Status: ${template.stage || 'Not set'}` : `Issue Tag: ${template.issueTagName || 'Not set'}${template.stage ? ` | Status filter: ${template.stage}` : ''}`}
                        {' | '}Track: {template.track}{' | '}{template.items.length} item{template.items.length === 1 ? '' : 's'}
                      </p>
                      <div className="button-row compact-actions">
                        <button onClick={() => setExpandedTemplateId(expandedTemplateId === template.id ? null : template.id)}>
                          {expandedTemplateId === template.id ? 'Hide Items' : 'View Items'}
                        </button>
                        <button onClick={() => startEditChecklistTemplate(template)}>Edit</button>
                        <button onClick={() => void deleteChecklistTemplate(template)}>Delete</button>
                      </div>
                      {expandedTemplateId === template.id && (
                        <div className="top-gap-small">
                          <div className="table-wrap">
                            <table className="compact-table">
                              <thead>
                                <tr><th>Order</th><th>Task</th><th>Workflow Status</th><th>Due Offset (days)</th><th>Actions</th></tr>
                              </thead>
                              <tbody>
                                {template.items.length === 0 ? <tr><td colSpan={5}>No items yet.</td></tr> : template.items.map((item) => (
                                  <tr key={item.id}>
                                    <td>{item.sortOrder}</td>
                                    <td>{item.task}</td>
                                    <td>{item.phase || 'Not set'}</td>
                                    <td>{item.dueOffsetDays ?? 'Not set'}</td>
                                    <td>
                                      <div className="button-row compact-actions row-actions">
                                        <button onClick={() => startEditTemplateItem(item)}>Edit</button>
                                        <button onClick={() => void deleteTemplateItem(item)}>Delete</button>
                                      </div>
                                    </td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          </div>
                          <div className="button-row compact-actions top-gap-small">
                            <button onClick={() => startNewTemplateItem(template)}>Add Item</button>
                          </div>
                        </div>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            </Panel>
          )}

          {settingsSection === 'backups' && (
            <Panel title="Backups">
              <p className="helper-text">A backup is taken automatically before every save, keeping the most recent 20. Use Restore to roll the database back to an earlier point — your current data is backed up first, so a bad restore can itself be undone.</p>
              <div className="button-row compact-actions top-gap-small">
                <button className="primary" onClick={() => void createBackupNow()}>Backup Now</button>
              </div>
              <div className="table-wrap top-gap-small">
                <table className="compact-table">
                  <thead>
                    <tr><th>Created</th><th>File</th><th>Size</th><th>Actions</th></tr>
                  </thead>
                  <tbody>
                    {backups.length === 0 ? <tr><td colSpan={4}>No backups yet.</td></tr> : backups.map((backup) => (
                      <tr key={backup.fileName}>
                        <td>{displayDateTime(backup.createdAt)}</td>
                        <td>{backup.fileName}</td>
                        <td>{formatFileSize(backup.sizeBytes)}</td>
                        <td><button onClick={() => void restoreFromBackup(backup)}>Restore</button></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </Panel>
          )}

          {settingsSection === 'documentPlatformTemplates' && (
            <Panel title="Document Templates">
              <div className="settings-subpanel">
                <h4>How this works</h4>
                <p className="helper-text"><strong>Make content and formatting changes in Word, then upload the edited file as a new version.</strong> This settings area manages the metadata around a template - not its content - so there's normally no reason to change anything here after the initial setup.</p>
                <p className="helper-text"><strong>Section key</strong>: a named block in the document, written as a <code>{'{{#Key}}'}...{'{{/Key}}'}</code> pair around the text in Word. Labeling it here is what lets the case Documents tab show it as a checklist item (and pre-check it when the case carries a matching issue tag) - it doesn't create the block itself, that has to already exist in the uploaded .docx.</p>
                <p className="helper-text"><strong>Runtime input / field key</strong>: tells the generator to prompt the attorney for a value at generation time (e.g. opposing counsel) instead of pulling it from the case record automatically. Like section keys, this only configures how an existing <code>{'{{FieldKey}}'}</code> token in the .docx gets its value - it doesn't add that token to the document.</p>
                <p className="helper-text"><strong>Overlap warning</strong>: a note that two sections cover similar ground. When both are checked at generation time, the case checklist shows the note so the attorney can decide whether to drop one - it never blocks generation on its own.</p>
              </div>
              <button className="compact-action-button" onClick={() => void loadPlatformTemplates()}>Load Templates</button>

              <h4 className="top-gap">{platformUploadKeyLocked ? `Upload New Version of "${platformUploadDraft.title}"` : 'Upload a New Template'}</h4>
              <div className="form-grid top-gap-small">
                <label><span>Template Key</span><input value={platformUploadDraft.templateKey} disabled={platformUploadKeyLocked} onChange={(e) => setPlatformUploadDraft({ ...platformUploadDraft, templateKey: e.target.value })} placeholder="e.g. settlement_memo_platform" /></label>
                <label><span>Title</span><input value={platformUploadDraft.title} onChange={(e) => setPlatformUploadDraft({ ...platformUploadDraft, title: e.target.value })} /></label>
                <label><span>Category</span><select value={platformUploadDraft.category} onChange={(e) => setPlatformUploadDraft({ ...platformUploadDraft, category: e.target.value })}><option value="">Select category…</option>{documentTemplateCategories.map((category) => <option key={category} value={category}>{category}</option>)}</select></label>
                <label className="full-span"><span>Description</span><input value={platformUploadDraft.description} onChange={(e) => setPlatformUploadDraft({ ...platformUploadDraft, description: e.target.value })} /></label>
                <label className="full-span"><span>File (.docx)</span><input type="file" accept=".docx" onChange={(e) => setPlatformUploadFile(e.target.files?.[0] ?? null)} /></label>
                <div className="full-span button-row compact-actions">
                  <button className="primary" onClick={() => void uploadPlatformTemplate()}>{platformUploadKeyLocked ? 'Upload New Version' : 'Upload Template'}</button>
                  {platformUploadKeyLocked && <button type="button" onClick={startNewPlatformTemplateUpload}>Cancel (upload a different template instead)</button>}
                </div>
              </div>

              <h4 className="top-gap">Templates</h4>
              <div className="table-wrap top-gap-small">
                <table className="compact-table">
                  <thead><tr><th>Title</th><th>Key</th><th>Category</th><th>Active Version</th><th>Type</th><th>Actions</th></tr></thead>
                  <tbody>
                    {platformTemplates.map((t) => (
                      <tr key={t.template.templateKey}>
                        <td>{t.template.title}</td>
                        <td>{t.template.templateKey}</td>
                        <td>{t.template.category}</td>
                        <td>{t.activeVersion ? `v${t.activeVersion.version}` : '—'}</td>
                        <td>{t.template.isBuiltin ? <span className="pill pill-neutral">Built-in</span> : <span className="pill pill-success">Custom</span>}</td>
                        <td>
                          <div className="button-row compact-actions row-actions">
                            <button className="primary" onClick={() => startUploadNewVersion(t)}>Upload New Version</button>
                            <button onClick={() => selectPlatformTemplate(t)}>Configure</button>
                            {!t.template.isBuiltin && <button onClick={() => void deletePlatformTemplate(t.template.templateKey)}>Delete</button>}
                          </div>
                        </td>
                      </tr>
                    ))}
                    {platformTemplates.length === 0 && <tr><td colSpan={6} className="helper-text">Click "Load Templates" to see the catalog.</td></tr>}
                  </tbody>
                </table>
              </div>

              {selectedPlatformTemplateKey && (() => {
                const selected = platformTemplates.find((t) => t.template.templateKey === selectedPlatformTemplateKey)
                if (!selected) return null
                return (
                  <div className="top-gap">
                    <h4>{selected.template.title} — Configuration</h4>
                    {selected.lintIssues.length > 0 && (
                      <div className="inline-message warn">{selected.lintIssues.map((issue) => <p key={issue}>{issue}</p>)}</div>
                    )}

                    <h5>Versions</h5>
                    <div className="button-row compact-actions">
                      {selected.versions.map((v) => (
                        <button key={v.id} className={v.isActive ? 'primary' : ''} disabled={v.isActive}
                          onClick={() => void activatePlatformVersion(selected.template.templateKey, v.version)}>
                          {v.isActive ? `v${v.version} Active` : `Activate v${v.version}`}
                        </button>
                      ))}
                    </div>

                    <h5 className="top-gap-small">Sections</h5>
                    <p className="helper-text">Each row must match a {'{{#Key}}'} / {'{{/Key}}'} pair in the uploaded .docx. Tying a section to an issue tag pre-checks it on the case Documents tab when that case carries the tag.</p>
                    <div className="table-wrap">
                      <table className="compact-table">
                        <thead><tr><th>Key</th><th>Label</th><th>Issue Tag</th><th>Actions</th></tr></thead>
                        <tbody>
                          {platformConfigDraft.sections.map((s) => (
                            <tr key={s.sectionKey}>
                              <td>{s.sectionKey}</td><td>{s.label}</td><td>{s.issueTagName || '—'}</td>
                              <td><button onClick={() => removeSectionDraft(s.sectionKey)}>Remove</button></td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                    <div className="form-grid top-gap-small">
                      <label><span>Section Key</span><input value={newSectionDraft.sectionKey} onChange={(e) => setNewSectionDraft({ ...newSectionDraft, sectionKey: e.target.value })} placeholder="matches {{#Key}} in the .docx" /></label>
                      <label><span>Label</span><input value={newSectionDraft.label} onChange={(e) => setNewSectionDraft({ ...newSectionDraft, label: e.target.value })} /></label>
                      <label><span>Issue Tag (optional)</span>
                        <select value={newSectionDraft.issueTagName} onChange={(e) => setNewSectionDraft({ ...newSectionDraft, issueTagName: e.target.value })}>
                          <option value="">None</option>
                          {allIssueTags.map((tag) => <option key={tag.id} value={tag.name}>{tag.name}</option>)}
                        </select>
                      </label>
                      <label className="full-span"><span>Description</span><input value={newSectionDraft.description} onChange={(e) => setNewSectionDraft({ ...newSectionDraft, description: e.target.value })} /></label>
                      <div className="full-span"><button onClick={addSectionDraft}>Add Section</button></div>
                    </div>

                    <h5 className="top-gap-small">Overlap Warnings</h5>
                    <p className="helper-text">When two sections both fire, the case checklist shows a warning so the attorney can drop one rather than filing duplicated questions.</p>
                    <div className="table-wrap">
                      <table className="compact-table">
                        <thead><tr><th>Section A</th><th>Section B</th><th>Note</th><th>Actions</th></tr></thead>
                        <tbody>
                          {platformConfigDraft.overlaps.map((o, i) => (
                            <tr key={`${o.sectionAKey}-${o.sectionBKey}`}>
                              <td>{o.sectionAKey}</td><td>{o.sectionBKey}</td><td>{o.note}</td>
                              <td><button onClick={() => removeOverlapDraft(i)}>Remove</button></td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                    <div className="form-grid top-gap-small">
                      <label><span>Section A</span>
                        <select value={newOverlapDraft.sectionAKey} onChange={(e) => setNewOverlapDraft({ ...newOverlapDraft, sectionAKey: e.target.value })}>
                          <option value="">Select...</option>
                          {platformConfigDraft.sections.map((s) => <option key={s.sectionKey} value={s.sectionKey}>{s.label}</option>)}
                        </select>
                      </label>
                      <label><span>Section B</span>
                        <select value={newOverlapDraft.sectionBKey} onChange={(e) => setNewOverlapDraft({ ...newOverlapDraft, sectionBKey: e.target.value })}>
                          <option value="">Select...</option>
                          {platformConfigDraft.sections.map((s) => <option key={s.sectionKey} value={s.sectionKey}>{s.label}</option>)}
                        </select>
                      </label>
                      <label className="full-span"><span>Note</span><input value={newOverlapDraft.note} onChange={(e) => setNewOverlapDraft({ ...newOverlapDraft, note: e.target.value })} /></label>
                      <div className="full-span"><button onClick={addOverlapDraft}>Add Overlap</button></div>
                    </div>

                    <h5 className="top-gap-small">Runtime Inputs</h5>
                    <p className="helper-text">Fields prompted for at generation time rather than resolved from the case (e.g. opposing counsel, a hearing date).</p>
                    <div className="table-wrap">
                      <table className="compact-table">
                        <thead><tr><th>Field Key</th><th>Label</th><th>Type</th><th>Required</th><th>Actions</th></tr></thead>
                        <tbody>
                          {platformConfigDraft.runtimeInputs.map((i) => (
                            <tr key={i.fieldKey}>
                              <td>{i.fieldKey}</td><td>{i.label}</td><td>{i.fieldType}</td><td>{i.isRequired ? 'Yes' : 'No'}</td>
                              <td><button onClick={() => removeRuntimeInputDraft(i.fieldKey)}>Remove</button></td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                    <div className="form-grid top-gap-small">
                      <label><span>Field Key</span><input value={newRuntimeInputDraft.fieldKey} onChange={(e) => setNewRuntimeInputDraft({ ...newRuntimeInputDraft, fieldKey: e.target.value })} placeholder="e.g. OpposingCounsel" /></label>
                      <label><span>Label</span><input value={newRuntimeInputDraft.label} onChange={(e) => setNewRuntimeInputDraft({ ...newRuntimeInputDraft, label: e.target.value })} /></label>
                      <label><span>Type</span>
                        <select value={newRuntimeInputDraft.fieldType} onChange={(e) => setNewRuntimeInputDraft({ ...newRuntimeInputDraft, fieldType: e.target.value })}>
                          <option value="text">Text</option><option value="date">Date</option><option value="number">Number</option><option value="textarea">Textarea</option>
                        </select>
                      </label>
                      <label className="toggle-inline"><span>Required</span><input type="checkbox" checked={newRuntimeInputDraft.isRequired} onChange={(e) => setNewRuntimeInputDraft({ ...newRuntimeInputDraft, isRequired: e.target.checked })} /></label>
                      <div className="full-span"><button onClick={addRuntimeInputDraft}>Add Runtime Input</button></div>
                    </div>

                    <button className="primary top-gap" onClick={() => void savePlatformConfiguration()}>Save Configuration</button>
                  </div>
                )
              })()}
            </Panel>
          )}

          {settingsSection === 'issueTags' && (
            <Panel title="Issue Tags">
              <p className="helper-text">Create, rename, and retire the issue-tag vocabulary cases use, and see which document-template sections a tag drives. Retiring a tag is a soft-delete: its name becomes available again, but case history keeps the original assignment.</p>
              <p className="helper-text">Tagging a case with an issue (e.g. "Timber") automatically pulls in that tag's interrogatory and request-for-production questions when generating Interrogatories or Requests for Admission for that case - see the "Used By" column below for which templates a tag drives, and Document Templates for the actual section content each tag inserts.</p>
              <button className="compact-action-button" onClick={() => void loadIssueTagUsage()}>Load Usage</button>

              <h4 className="top-gap">Create a Tag</h4>
              <div className="form-grid top-gap-small">
                <label><span>Name</span><input value={newIssueTagDraft.name} onChange={(e) => setNewIssueTagDraft({ ...newIssueTagDraft, name: e.target.value })} /></label>
                <label><span>Category</span><select value={newIssueTagDraft.category} onChange={(e) => setNewIssueTagDraft({ ...newIssueTagDraft, category: e.target.value })}><option value="">Select category…</option>{issueTagCategories.map((category) => <option key={category} value={category}>{category}</option>)}</select></label>
                <label className="full-span"><span>Description</span><input value={newIssueTagDraft.description} onChange={(e) => setNewIssueTagDraft({ ...newIssueTagDraft, description: e.target.value })} /></label>
                <div className="full-span"><button className="primary" onClick={() => void createIssueTagFromSettings()}>Create Tag</button></div>
              </div>

              <h4 className="top-gap">Existing Tags</h4>
              <div className="table-wrap top-gap-small">
                <table className="compact-table">
                  <thead><tr><th>Name</th><th>Category</th><th>Description</th><th>Used By</th><th>Actions</th></tr></thead>
                  <tbody>
                    {allIssueTags.map((tag) => {
                      const usage = issueTagUsage.find((u) => u.tagName.toLowerCase() === tag.name.toLowerCase())
                      const isEditing = issueTagEditDraft?.id === tag.id
                      return (
                        <tr key={tag.id}>
                          {isEditing && issueTagEditDraft ? (
                            <>
                              <td><input value={issueTagEditDraft.name} onChange={(e) => setIssueTagEditDraft({ ...issueTagEditDraft, name: e.target.value })} /></td>
                              <td><select value={issueTagEditDraft.category} onChange={(e) => setIssueTagEditDraft({ ...issueTagEditDraft, category: e.target.value })}>{issueTagCategories.map((category) => <option key={category} value={category}>{category}</option>)}</select></td>
                              <td><input value={issueTagEditDraft.description} onChange={(e) => setIssueTagEditDraft({ ...issueTagEditDraft, description: e.target.value })} /></td>
                              <td>{usage ? usage.templateTitles.join(', ') : '—'}</td>
                              <td>
                                <div className="button-row compact-actions row-actions">
                                  <button className="primary" onClick={() => void saveIssueTagRename()}>Save</button>
                                  <button onClick={() => setIssueTagEditDraft(null)}>Cancel</button>
                                </div>
                              </td>
                            </>
                          ) : (
                            <>
                              <td>{tag.name}</td>
                              <td>{tag.category}</td>
                              <td>{tag.description}</td>
                              <td>{usage ? usage.templateTitles.join(', ') : '—'}</td>
                              <td>
                                <div className="button-row compact-actions row-actions">
                                  <button onClick={() => setIssueTagEditDraft({ id: tag.id, name: tag.name, description: tag.description ?? '', category: tag.category ?? '' })}>Rename</button>
                                  <button onClick={() => void retireIssueTagFromSettings(tag.id)}>Retire</button>
                                </div>
                              </td>
                            </>
                          )}
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            </Panel>
          )}

          {settingsSection === 'deadlineTemplates' && (
            <Panel title="Deadline Templates">
              <p className="helper-text">Configure calculated deadlines by anchor, offset, track, and severity. Generated deadlines retain structured source provenance and manual overrides.</p>
              <button className="primary" onClick={() => setDeadlineTemplateDraft({id:0,name:'',triggerField:'filing_date',offsetDays:0,title:'',severity:'normal',track:'Any',active:true})}>Add Deadline Template</button>
              {deadlineTemplateDraft && <div className="form-grid top-gap-small"><label><span>Name</span><input value={deadlineTemplateDraft.name} onChange={e=>setDeadlineTemplateDraft({...deadlineTemplateDraft,name:e.target.value})}/></label><label><span>Title</span><input value={deadlineTemplateDraft.title} onChange={e=>setDeadlineTemplateDraft({...deadlineTemplateDraft,title:e.target.value})}/></label><label><span>Anchor</span><select value={deadlineTemplateDraft.triggerField} onChange={e=>setDeadlineTemplateDraft({...deadlineTemplateDraft,triggerField:e.target.value})}><option value="filing_date">Filing date</option><option value="trial_date">Trial date</option><option value="service_perfected_date">Service perfected date</option></select></label><label><span>Offset days</span><input type="number" value={deadlineTemplateDraft.offsetDays} onChange={e=>setDeadlineTemplateDraft({...deadlineTemplateDraft,offsetDays:Number(e.target.value)})}/></label><label><span>Track</span><select value={deadlineTemplateDraft.track} onChange={e=>setDeadlineTemplateDraft({...deadlineTemplateDraft,track:e.target.value})}><option>Any</option>{caseTracks.map(x=><option key={x}>{x}</option>)}</select></label><label><span>Severity</span><select value={deadlineTemplateDraft.severity} onChange={e=>setDeadlineTemplateDraft({...deadlineTemplateDraft,severity:e.target.value})}>{deadlineSeverities.map(x=><option key={x}>{x}</option>)}</select></label><label className="toggle-inline"><span>Active</span><input type="checkbox" checked={deadlineTemplateDraft.active} onChange={e=>setDeadlineTemplateDraft({...deadlineTemplateDraft,active:e.target.checked})}/></label><div className="button-row full-span"><button className="primary" onClick={()=>void saveDeadlineTemplate()}>Save</button><button onClick={()=>setDeadlineTemplateDraft(null)}>Cancel</button></div></div>}
              <div className="table-wrap top-gap-small"><table className="compact-table"><thead><tr><th>Name</th><th>Title</th><th>Calculation</th><th>Track</th><th>Actions</th></tr></thead><tbody>{deadlineTemplates.map(t=><tr key={t.id}><td>{t.name}</td><td>{t.title}</td><td>{t.triggerField} {t.offsetDays>=0?'+':''}{t.offsetDays} days</td><td>{t.track}</td><td><button onClick={()=>setDeadlineTemplateDraft({...t})}>Edit</button></td></tr>)}</tbody></table></div>
            </Panel>
          )}

          {settingsSection === 'about' && (
            <Panel title="About / IT Notes">
              <p>Court documents (Interrogatories, Requests for Admission, Judgment, Settlement Justification) are generated from real <code>.docx</code> templates under <code>templates/documents/platform</code> through a native C# merge engine — section and loop content and merge fields are resolved directly against the OpenXml document tree, with no third-party templating library and no external Word automation. CSV and Excel (.xlsx/.xlsm) import are both supported. All data stays local.</p>
              <p>SQLite is the active runtime database. SQL Server support is already built as a parallel, provider-selected implementation — including migration scripts, a dedicated database migrator, and reconciliation services — for an eventual IT-supported shared deployment; SQL Server activation remains intentionally disabled until reconciliation, identity, and authorization work is complete.</p>
            </Panel>
          )}

          {/* Dev-only settings section - strip this entire block, the 'developer' SettingsSectionKey,
              and its settingsCategories entry before a real release. */}
          {settingsSection === 'developer' && (
            <Panel title="Developer">
              <p className="helper-text">Dev-only tools. Not part of the shipped product - strip this section before a real release.</p>
              <h4 className="top-gap">Server Shutdown</h4>
              <p className="helper-text">A clean way to stop the local server during development instead of killing the process via Task Manager.</p>
              <div className="button-row compact-actions top-gap-small">
                <button type="button" onClick={() => void exitCasePlanner()} disabled={shutdownBusy} title="Stop the local Case Planner server">
                  {shutdownBusy ? 'Exiting…' : 'Exit Case Planner'}
                </button>
              </div>
            </Panel>
          )}
            </section>
          </div>
        </main>
      )}

      <footer className="app-status-bar">
        <span className="app-status-label">Recent activity</span>
        <span>{message}</span>
      </footer>
    </div>
  )
}

function NumericField({ value, onCommit, placeholder, money }: { value: number | null | undefined; onCommit: (value: number | null) => void; placeholder?: string; money?: boolean }) {
  const [text, setText] = useState(value == null ? '' : String(value))

  useEffect(() => {
    setText(value == null ? '' : String(value))
  }, [value])

  function commit() {
    const cleaned = text.replaceAll(',', '').replace('$', '').trim()
    if (cleaned === '') {
      onCommit(null)
      return
    }
    const parsed = Number(cleaned)
    onCommit(Number.isNaN(parsed) ? null : parsed)
  }

  const input = (
    <input
      value={text}
      inputMode="decimal"
      onChange={(event) => setText(event.target.value)}
      onBlur={commit}
      placeholder={placeholder}
    />
  )

  if (!money) return input

  return (
    <div className="money-input">
      <span className="money-input-prefix">$</span>
      {input}
    </div>
  )
}

export function Panel({ title, headerAction, children, className }: { title: string; headerAction?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <section className={className ? `panel ${className}` : 'panel'}>
      <div className="panel-header">
        <h3>{title}</h3>
        {headerAction}
      </div>
      <div className="panel-body">{children}</div>
    </section>
  )
}

export function CollapsiblePanel({ title, defaultOpen = true, children }: { title: string; defaultOpen?: boolean; children: ReactNode }) {
  const [open, setOpen] = useState(defaultOpen)
  return (
    <Panel title={title} headerAction={<button className="link-button" onClick={() => setOpen((current) => !current)}>{open ? 'Collapse' : 'Expand'}</button>}>
      {open && children}
    </Panel>
  )
}

export function ModalShell({ title, children, onClose }: { title: string; children: ReactNode; onClose: () => void }) {
  return (
    <div className="modal-overlay" role="presentation">
      <section className="modal-shell" role="dialog" aria-modal="true" aria-label={title}>
        <div className="modal-header">
          <div>
            <p className="eyebrow dark">In-Page Editor</p>
            <h3>{title}</h3>
          </div>
          <button className="icon-button" onClick={onClose} aria-label="Close dialog">Close</button>
        </div>
        <div className="modal-body">{children}</div>
      </section>
    </div>
  )
}

export function StatCard({ label, value, tone = 'neutral', active = false, onClick }: { label: string; value: string; tone?: 'neutral' | 'warn' | 'ok'; active?: boolean; onClick?: () => void }) {
  const className = `stat-card ${tone}${active ? ' active' : ''}${onClick ? ' interactive' : ''}`

  if (onClick) {
    return (
      <button type="button" className={className} onClick={onClick}>
        <span>{label}</span>
        <strong>{value}</strong>
      </button>
    )
  }

  return (
    <article className={className}>
      <span>{label}</span>
      <strong>{value}</strong>
    </article>
  )
}

function PathField({ label, value, className = '' }: { label: string; value: string; className?: string }) {
  const classes = ['path-field', className].filter(Boolean).join(' ')
  return (
    <div className={classes}>
      <label>
        <span>{label}</span>
        <div className="path-field-row">
          <input readOnly value={value} />
          <button type="button" onClick={() => navigator.clipboard.writeText(value)}>Copy Path</button>
        </div>
      </label>
      <p className="helper-text">Read-only local path.</p>
    </div>
  )
}

export default App
