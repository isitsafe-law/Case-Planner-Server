import { useMemo, useState } from 'react'
import type { CaseRecord } from '../App'
import { Btn } from '../ui/Btn'
import { downloadCsv } from '../ui/csvExport'
import { EmptyState } from './EmptyState'
import { daysPending } from './SettlementAuthoritySection'
import { formatCurrencyOrDash } from './dashboardAggregation'
import type { SettlementAuthorityRequestRecord } from './types'

export const DEFAULT_ACTIVITY_THRESHOLD_DAYS = 14
export const DEFAULT_APPROVAL_THRESHOLD_DAYS = 5

// The four rule "types", in the fixed group order the combined list is sorted by (see
// buildNeedsAttentionRows's own sort-order comment below). Three other candidate rules from the
// original spec - no settlement evaluation recorded, settlements approaching/exceeding granted
// authority, status hasn't changed longer than typical dwell time - were investigated and confirmed
// with the user to have no backing data model field, so they are deliberately not built here and
// are never referenced in this UI.
type RuleType = 'service' | 'activity' | 'feeShift' | 'approval'

export type NeedsAttentionRow = {
  key: string
  ruleType: RuleType
  reason: string
  // null renders as "—" - the fee-shift reference rule has no age concept at all (no status-change
  // history exists to compute "how long in Trial Preparation" from - confirmed gap, not a bug).
  age: number | null
  attorney: string
  caseId: number
  jobNumber: string
  tract: string
  caseName: string
  // Only used as a secondary sort key within the fee-shift group (see buildNeedsAttentionRows).
  sortSecondary?: number
}

// Whole days elapsed from a date string to `now` (default: actual now) - same floor-not-round,
// never-negative convention as SettlementAuthoritySection.tsx's daysPending, generalized to any
// basis date rather than only requestedAt.
function daysSince(dateStr: string, now: Date = new Date()): number {
  const diffMs = now.getTime() - new Date(dateStr).getTime()
  return Math.max(0, Math.floor(diffMs / 86_400_000))
}

// Rule (a): past the 60-day service soft flag. Soft-flag language only - never "overdue", "missed",
// or "violation"; the 60-day window is explicitly a soft check-in point, not a deadline (hard ARDOT
// terminology rule, not a style preference).
export function serviceSoftFlagRow(record: CaseRecord, now: Date = new Date()): NeedsAttentionRow | null {
  if (record.servicePerfected) return null
  const basis = record.serviceDeadlineBasisDate || record.filingDate
  if (!basis) return null
  const ageDays = daysSince(basis, now)
  if (ageDays <= 60) return null
  return {
    key: `service-${record.id}`,
    ruleType: 'service',
    reason: 'Service pending beyond the 60-day check-in point',
    age: ageDays,
    attorney: record.assignedAttorney || 'Unassigned',
    caseId: record.id,
    jobNumber: record.jobNumber || '',
    tract: record.tract || '',
    caseName: record.caseName,
  }
}

// Rule (b): no recorded activity in N days (default 14, configurable). A closed case going quiet is
// not an exception, so 'Resolved / Closed' cases are skipped. A case with no
// lastMeaningfulActivityDate at all (never touched) is always flagged - arguably the most
// concerning case, not one to exclude - with its age computed from dateOpened when available, or
// left null ("—") rather than fabricated or silently dropped.
export function staleActivityRow(record: CaseRecord, thresholdDays: number, now: Date = new Date()): NeedsAttentionRow | null {
  if ((record.caseStatus || 'Pipeline') === 'Resolved / Closed') return null

  let ageDays: number | null
  if (record.lastMeaningfulActivityDate) {
    ageDays = daysSince(record.lastMeaningfulActivityDate, now)
    if (ageDays <= thresholdDays) return null
  } else {
    ageDays = record.dateOpened ? daysSince(record.dateOpened, now) : null
  }

  return {
    key: `activity-${record.id}`,
    ruleType: 'activity',
    reason: `No recorded activity in over ${thresholdDays} days.`,
    age: ageDays,
    attorney: record.assignedAttorney || 'Unassigned',
    caseId: record.id,
    jobNumber: record.jobNumber || '',
    tract: record.tract || '',
    caseName: record.caseName,
  }
}

// Rule (c): fee-shift exposure reference. Jury-verdict scope only - Ark. Code Ann. § 27-67-317(b)'s
// 20%-above threshold never applies to a settlement, only a jury verdict - so this is a forward-
// looking reference figure only ("if a jury verdict here exceeded this figure..."), never a claim
// that a verdict has occurred or that fees are owed.
export function feeShiftReferenceRow(record: CaseRecord): NeedsAttentionRow | null {
  if ((record.caseStatus || 'Pipeline') !== 'Trial Preparation') return null
  if (record.depositAmount == null) return null
  const thresholdFigure = record.depositAmount * 1.2
  return {
    key: `feeshift-${record.id}`,
    ruleType: 'feeShift',
    reason: `Trial Preparation - fee-shift reference: deposit ${formatCurrencyOrDash(record.depositAmount)}, 20%-above figure ${formatCurrencyOrDash(thresholdFigure)}`,
    age: null,
    attorney: record.assignedAttorney || 'Unassigned',
    caseId: record.id,
    jobNumber: record.jobNumber || '',
    tract: record.tract || '',
    caseName: record.caseName,
    sortSecondary: record.depositAmount,
  }
}

// Rule (d): Settlement Authority approval requests pending beyond a configurable age (default 5
// days). Attorney falls back to the joined case's assignedAttorney when requestingAttorney is blank.
export function pendingApprovalRow(
  request: SettlementAuthorityRequestRecord,
  matchedCase: CaseRecord | undefined,
  thresholdDays: number,
  now: Date = new Date(),
): NeedsAttentionRow | null {
  if (request.status !== 'Pending') return null
  const pending = daysPending(request.requestedAt, now)
  if (pending <= thresholdDays) return null
  return {
    key: `approval-${request.id}`,
    ruleType: 'approval',
    reason: `Settlement Authority request pending beyond ${thresholdDays} days.`,
    age: pending,
    attorney: request.requestingAttorney || matchedCase?.assignedAttorney || 'Unassigned',
    caseId: request.caseId,
    jobNumber: matchedCase?.jobNumber || '',
    tract: matchedCase?.tract || '',
    caseName: matchedCase?.caseName || `Case ${request.caseId}`,
  }
}

const RULE_ORDER: RuleType[] = ['service', 'activity', 'feeShift', 'approval']

// Builds the combined, single ranked exception list (not four separate tables) - a case can appear
// more than once if it triggers more than one rule, which is expected, not a bug to deduplicate.
//
// Sort order: grouped by rule type in RULE_ORDER (service soft-flag, then stale-activity, then
// fee-shift reference, then pending-approval) so a manager scanning top to bottom sees like
// exceptions together, rather than four rule types interleaved by a single shared "severity" number
// this app has no real basis for computing (see design-system's "no unexplained scores" precedent,
// matching the Attorney Dashboard's own documented approach). Within each group, oldest/most-
// concerning first (age descending) - except the fee-shift group, which has no age concept at all
// and instead sorts by deposit amount descending (larger exposure first), matching
// SettlementAuthoritySection.tsx's own documented sort-order comment style.
export function buildNeedsAttentionRows(
  allCases: CaseRecord[],
  settlementAuthorityRequests: SettlementAuthorityRequestRecord[],
  activityThresholdDays: number,
  approvalThresholdDays: number,
  now: Date = new Date(),
): NeedsAttentionRow[] {
  const caseById = new Map(allCases.map((record) => [record.id, record]))
  const rows: NeedsAttentionRow[] = []

  for (const record of allCases) {
    const row = serviceSoftFlagRow(record, now)
    if (row) rows.push(row)
  }
  for (const record of allCases) {
    const row = staleActivityRow(record, activityThresholdDays, now)
    if (row) rows.push(row)
  }
  for (const record of allCases) {
    const row = feeShiftReferenceRow(record)
    if (row) rows.push(row)
  }
  for (const request of settlementAuthorityRequests) {
    const row = pendingApprovalRow(request, caseById.get(request.caseId), approvalThresholdDays, now)
    if (row) rows.push(row)
  }

  return rows.sort((a, b) => {
    const groupDiff = RULE_ORDER.indexOf(a.ruleType) - RULE_ORDER.indexOf(b.ruleType)
    if (groupDiff !== 0) return groupDiff
    if (a.ruleType === 'feeShift') return (b.sortSecondary ?? 0) - (a.sortSecondary ?? 0)
    return (b.age ?? -1) - (a.age ?? -1)
  })
}

function exportCsv(rows: NeedsAttentionRow[]) {
  const csvRows = rows.map((row) => ({
    Reason: row.reason,
    Age: row.age == null ? '' : row.age,
    Attorney: row.attorney,
    'Job + Tract': [row.jobNumber, row.tract].filter(Boolean).join(' · '),
    'Case Name': row.caseName,
  }))
  downloadCsv(`Needs_Attention_${new Date().toISOString().slice(0, 10)}.csv`, csvRows)
}

// Short category pill + the full reason sentence as normal wrapped text below it - .pill's
// white-space: nowrap makes it unsuitable for a full sentence on its own (it would force a single
// very wide, non-wrapping capsule), so only a short category label goes inside the pill itself.
const RULE_PILL_CLASS: Record<RuleType, string> = {
  service: 'pill-warn',
  activity: 'pill-danger',
  feeShift: 'pill-neutral',
  approval: 'pill-primary',
}

const RULE_TYPE_LABEL: Record<RuleType, string> = {
  service: 'Service',
  activity: 'Activity',
  feeShift: 'Fee-Shift Reference',
  approval: 'Approval',
}

export function NeedsAttentionTab({
  allCases,
  settlementAuthorityRequests,
  onOpenCase,
}: {
  allCases: CaseRecord[]
  settlementAuthorityRequests: SettlementAuthorityRequestRecord[]
  onOpenCase: (caseId: number) => void
}) {
  const [activityThresholdDays, setActivityThresholdDays] = useState(DEFAULT_ACTIVITY_THRESHOLD_DAYS)
  const [approvalThresholdDays, setApprovalThresholdDays] = useState(DEFAULT_APPROVAL_THRESHOLD_DAYS)

  const rows = useMemo(
    () => buildNeedsAttentionRows(allCases, settlementAuthorityRequests, activityThresholdDays, approvalThresholdDays),
    [allCases, settlementAuthorityRequests, activityThresholdDays, approvalThresholdDays],
  )

  return (
    <div>
      <div className="inline-quick-form" style={{ marginBottom: '0.85rem' }}>
        <label>
          <span>No-activity threshold (days)</span>
          <input
            type="number"
            min={1}
            value={activityThresholdDays}
            onChange={(event) => setActivityThresholdDays(Math.max(1, Number(event.target.value) || 1))}
          />
        </label>
        <label>
          <span>Approval-pending threshold (days)</span>
          <input
            type="number"
            min={1}
            value={approvalThresholdDays}
            onChange={(event) => setApprovalThresholdDays(Math.max(1, Number(event.target.value) || 1))}
          />
        </label>
        <Btn onClick={() => exportCsv(rows)} disabled={rows.length === 0}>Export CSV</Btn>
      </div>
      <p className="helper-text" style={{ marginBottom: '0.6rem' }}>
        Both thresholds apply only to this view for this session - they are not saved anywhere.
      </p>

      {rows.length === 0 ? (
        <EmptyState title="Nothing needs attention right now." description="No case currently trips the service, activity, fee-shift, or approval-aging checks below the thresholds above." />
      ) : (
        <div className="table-wrap">
          <table className="ui-table compact-table">
            <thead>
              <tr>
                <th>Reason</th>
                <th>Age (days)</th>
                <th>Attorney</th>
                <th>Job + Tract</th>
                <th>Case Name</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.key}>
                  <td>
                    <span className={`pill ${RULE_PILL_CLASS[row.ruleType]}`}>{RULE_TYPE_LABEL[row.ruleType]}</span>
                    <div style={{ marginTop: '0.25rem' }}>{row.reason}</div>
                  </td>
                  <td>{row.age == null ? '—' : row.age}</td>
                  <td>{row.attorney}</td>
                  <td>{[row.jobNumber, row.tract].filter(Boolean).join(' · ') || '—'}</td>
                  <td>{row.caseName}</td>
                  <td><Btn size="sm" onClick={() => onOpenCase(row.caseId)}>Open Case</Btn></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
