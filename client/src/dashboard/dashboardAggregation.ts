// Manager/Administrator Dashboard Milestone 5, part 2 of 2 (By Attorney / By Job / Needs Attention).
// Pure, exported computation helpers shared by ByAttorneyTab.tsx and ByJobTab.tsx - kept separate
// from either component file so neither tab ends up as the "original" copy the other reimplements
// from, matching this feature's existing precedent of exporting pure math for unit testing (see
// SettlementAuthoritySection.tsx's daysPending / settlementAuthorityDelta).

import type { CaseRecord, Hearing } from '../App'

// The six lifecycle-stage caseStatus values this feature buckets into, in lifecycle order. Note:
// the legacy pre-intake "Triage" status (still recognized elsewhere in this app, e.g. isOpenCase/
// isOpenForDivision) is deliberately NOT a seventh bucket here - the spec for this feature defines
// exactly six buckets, so bucketCaseStatus below folds Triage (and anything else unrecognized)
// into Pipeline rather than silently dropping it from every count.
export const CASE_STATUS_ORDER = [
  'Pipeline',
  'Filed / Service Pending',
  'Active Litigation',
  'Settlement Pending',
  'Trial Preparation',
  'Resolved / Closed',
] as const

export type CaseStatusBucket = (typeof CASE_STATUS_ORDER)[number]

const CASE_STATUS_SET: ReadonlySet<string> = new Set(CASE_STATUS_ORDER)

// Buckets a case's caseStatus into one of the six canonical values above. Blank/missing defaults to
// Pipeline, matching this app's universal `record.caseStatus || 'Pipeline'` convention; any other
// unrecognized value (legacy "Triage" included) also folds into Pipeline - a deliberate judgment
// call, documented above, rather than inventing a seventh bucket this feature's spec doesn't define.
export function bucketCaseStatus(record: CaseRecord): CaseStatusBucket {
  const raw = record.caseStatus || 'Pipeline'
  return CASE_STATUS_SET.has(raw) ? (raw as CaseStatusBucket) : 'Pipeline'
}

export type StatusCount = { status: CaseStatusBucket; count: number }

// Counts by status, always returned in CASE_STATUS_ORDER (including zero-count buckets) so every
// caller can render all six segments/columns without checking for missing keys.
export function statusDistribution(records: CaseRecord[]): StatusCount[] {
  const counts = new Map<CaseStatusBucket, number>(CASE_STATUS_ORDER.map((status) => [status, 0]))
  for (const record of records) {
    const bucket = bucketCaseStatus(record)
    counts.set(bucket, (counts.get(bucket) || 0) + 1)
  }
  return CASE_STATUS_ORDER.map((status) => ({ status, count: counts.get(status) || 0 }))
}

export type NextHardDate = { date: string; label: string }

// The earliest "hard date" across a group of cases: each case's next open deadline
// (nextDeadlineDate/nextDeadlineTitle, already server-computed), each Hearing belonging to one of
// `cases` (hearingDate, labeled with the hearing's own title or eventType), and each case's
// trialDate (always labeled with the correct ARDOT term - never generic "Trial"). Returns null when
// none of the three sources produced a candidate. Dates are compared as their YYYY-MM-DD prefix -
// lexicographic order matches chronological order for ISO date strings, the same assumption
// ManagerCalendarTab.tsx's toEpochDay relies on. Deliberately does not exclude past-due dates: an
// open deadline that has slipped is still the most useful "next hard date" to surface to a manager.
export function nextHardDate(cases: CaseRecord[], allHearings: Hearing[]): NextHardDate | null {
  const caseIds = new Set(cases.map((c) => c.id))
  let best: NextHardDate | null = null

  function consider(date: string | null | undefined, label: string) {
    if (!date) return
    if (!best || date.slice(0, 10) < best.date.slice(0, 10)) best = { date, label }
  }

  for (const record of cases) {
    consider(record.nextDeadlineDate, record.nextDeadlineTitle || 'Deadline')
    consider(record.trialDate, 'Jury Trial on Just Compensation')
  }
  for (const hearing of allHearings) {
    if (!caseIds.has(hearing.caseId)) continue
    consider(hearing.hearingDate, hearing.title || hearing.eventType || 'Hearing')
  }
  return best
}

// Local formatter matching SettlementAuthoritySection.tsx's own private formatCurrency (a null
// amount renders as "—", not "$0.00" or "Not set") - kept as its own copy per that file's own
// precedent of not sharing this tiny formatter across dashboard files.
export function formatCurrencyOrDash(value?: number | null): string {
  if (value == null) return '—'
  return `$${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}
