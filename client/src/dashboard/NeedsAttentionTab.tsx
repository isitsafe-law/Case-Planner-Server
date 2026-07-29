import { useMemo, useState } from 'react'
import type { CaseRecord } from '../App'
import { Btn } from '../ui/Btn'
import { downloadCsv } from '../ui/csvExport'
import { EmptyState } from './EmptyState'
import { formatCurrencyOrDash } from './dashboardAggregation'
import { computePreFilingStallInfo } from './preFilingStallDetection'
import type { PreFilingMilestoneRecord, ReviewNoteRecord } from './types'

export const DEFAULT_ACTIVITY_THRESHOLD_DAYS = 14
export const DEFAULT_PRE_FILING_STALL_THRESHOLD_DAYS = 7

// The four rule "types", in the fixed group order the combined list is sorted by (see
// buildNeedsAttentionRows's own sort-order comment below). Three other candidate rules from the
// original spec - no settlement evaluation recorded, settlements approaching/exceeding granted
// authority, status hasn't changed longer than typical dwell time - were investigated and confirmed
// with the user to have no backing data model field, so they are deliberately not built here and
// are never referenced in this UI. A fifth rule (pending Settlement Authority approvals) was removed
// along with the manager dashboard's Approvals surface - see ManagerDashboard.tsx.
type RuleType = 'preFilingStall' | 'service' | 'activity' | 'feeShift'

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

// Whole days elapsed from a date string to `now` (default: actual now) - floor-not-round,
// never-negative.
function daysSince(dateStr: string, now: Date = new Date()): number {
  const diffMs = now.getTime() - new Date(dateStr).getTime()
  return Math.max(0, Math.floor(diffMs / 86_400_000))
}

// Rule (0, final implementation item 3): a Pipeline tract stalled on pre-filing sign-off beyond a
// configurable threshold (default 7 days) - reads the SAME shared detector IncomingPipelinePanel.tsx
// uses (computePreFilingStallInfo), so a case appears here for exactly the reason its Pipeline Health
// row shows, never a separately-derived one. Only applies to a case actually in Pipeline status
// (milestones/review notes are meaningless once filed) and only when there's an age to measure
// (daysStalled null - nothing marked, no review notes - never fires here, since there's nothing yet
// to call a "stall").
export function preFilingStallRow(
  record: CaseRecord,
  milestones: PreFilingMilestoneRecord[],
  reviewNotes: ReviewNoteRecord[],
  thresholdDays: number,
  now: Date = new Date(),
): NeedsAttentionRow | null {
  if ((record.caseStatus || 'Pipeline') !== 'Pipeline') return null
  const info = computePreFilingStallInfo(record.id, milestones, reviewNotes, now)
  if (info.daysStalled == null || info.daysStalled <= thresholdDays) return null
  return {
    key: `prefiling-stall-${record.id}`,
    ruleType: 'preFilingStall',
    reason: info.label,
    age: info.daysStalled,
    attorney: record.assignedAttorney || 'Unassigned',
    caseId: record.id,
    jobNumber: record.jobNumber || '',
    tract: record.tract || '',
    caseName: record.caseName,
  }
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

const RULE_ORDER: RuleType[] = ['preFilingStall', 'service', 'activity', 'feeShift']

// Builds the combined, single ranked exception list (not four separate tables) - a case can appear
// more than once if it triggers more than one rule, which is expected, not a bug to deduplicate.
//
// Sort order: grouped by rule type in RULE_ORDER (pre-filing stall, then service soft-flag, then
// stale-activity, then fee-shift reference) so a manager scanning top to bottom sees like exceptions
// together, rather than four rule types interleaved by a single shared "severity" number this app
// has no real basis for computing (see design-system's "no unexplained scores" precedent, matching
// the Attorney Dashboard's own documented approach). Within each group, oldest/most-concerning first
// (age descending) - except the fee-shift group, which has no age concept at all and instead sorts
// by deposit amount descending (larger exposure first).
export function buildNeedsAttentionRows(
  allCases: CaseRecord[],
  preFilingMilestones: PreFilingMilestoneRecord[],
  reviewNotes: ReviewNoteRecord[],
  activityThresholdDays: number,
  preFilingStallThresholdDays: number,
  now: Date = new Date(),
): NeedsAttentionRow[] {
  const rows: NeedsAttentionRow[] = []

  for (const record of allCases) {
    const row = preFilingStallRow(record, preFilingMilestones, reviewNotes, preFilingStallThresholdDays, now)
    if (row) rows.push(row)
  }
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
  preFilingStall: 'pill-warn',
  service: 'pill-warn',
  activity: 'pill-danger',
  feeShift: 'pill-neutral',
}

const RULE_TYPE_LABEL: Record<RuleType, string> = {
  preFilingStall: 'Pre-Filing Stall',
  service: 'Service',
  activity: 'Activity',
  feeShift: 'Fee-Shift Reference',
}

export function NeedsAttentionTab({
  allCases,
  preFilingMilestones,
  reviewNotes,
  onOpenCase,
}: {
  allCases: CaseRecord[]
  preFilingMilestones: PreFilingMilestoneRecord[]
  reviewNotes: ReviewNoteRecord[]
  onOpenCase: (caseId: number) => void
}) {
  const [activityThresholdDays, setActivityThresholdDays] = useState(DEFAULT_ACTIVITY_THRESHOLD_DAYS)
  const [preFilingStallThresholdDays, setPreFilingStallThresholdDays] = useState(DEFAULT_PRE_FILING_STALL_THRESHOLD_DAYS)

  const rows = useMemo(
    () => buildNeedsAttentionRows(
      allCases, preFilingMilestones, reviewNotes,
      activityThresholdDays, preFilingStallThresholdDays,
    ),
    [allCases, preFilingMilestones, reviewNotes, activityThresholdDays, preFilingStallThresholdDays],
  )

  return (
    <div>
      <div className="inline-quick-form" style={{ marginBottom: '0.85rem' }}>
        <label>
          <span>Pre-filing stall threshold (days)</span>
          <input
            type="number"
            min={1}
            value={preFilingStallThresholdDays}
            onChange={(event) => setPreFilingStallThresholdDays(Math.max(1, Number(event.target.value) || 1))}
          />
        </label>
        <label>
          <span>No-activity threshold (days)</span>
          <input
            type="number"
            min={1}
            value={activityThresholdDays}
            onChange={(event) => setActivityThresholdDays(Math.max(1, Number(event.target.value) || 1))}
          />
        </label>
        <Btn onClick={() => exportCsv(rows)} disabled={rows.length === 0}>Export CSV</Btn>
      </div>
      <p className="helper-text" style={{ marginBottom: '0.6rem' }}>
        Both thresholds apply only to this view for this session - they are not saved anywhere.
      </p>

      {rows.length === 0 ? (
        <EmptyState title="Nothing needs attention right now." description="No case currently trips the pre-filing stall, service, activity, or fee-shift checks below the thresholds above." />
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
