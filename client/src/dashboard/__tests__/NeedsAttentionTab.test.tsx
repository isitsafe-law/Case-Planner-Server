import { describe, expect, it } from 'vitest'
import type { CaseRecord } from '../../App'
import {
  buildNeedsAttentionRows,
  feeShiftReferenceRow,
  preFilingStallRow,
  serviceSoftFlagRow,
  staleActivityRow,
} from '../NeedsAttentionTab'
import type { PreFilingMilestoneRecord, ReviewNoteRecord } from '../types'

const NOW = new Date('2026-07-27T12:00:00Z')

function makeCase(overrides: Partial<CaseRecord> = {}): CaseRecord {
  return {
    id: 1,
    caseNumber: 'CV-2026-1',
    caseName: 'State v. Doe',
    jobNumber: 'JOB1',
    tract: '1',
    county: 'Pulaski',
    status: 'Open',
    serviceRequired: true,
    servicePerfected: false,
    assignedAttorney: 'Jane Roe',
    ...overrides,
  }
}

function makeMilestone(overrides: Partial<PreFilingMilestoneRecord> = {}): PreFilingMilestoneRecord {
  return { id: 1, caseId: 1, milestone: 'PleadingsPackageSent', isMarked: true, ...overrides }
}

describe('serviceSoftFlagRow (rule a)', () => {
  it('returns null once service is perfected', () => {
    expect(serviceSoftFlagRow(makeCase({ servicePerfected: true, filingDate: '2026-01-01' }), NOW)).toBeNull()
  })

  it('returns null when both basis dates are missing', () => {
    expect(serviceSoftFlagRow(makeCase({ servicePerfected: false }), NOW)).toBeNull()
  })

  it('returns null within the 60-day window', () => {
    // 2026-06-10 to 2026-07-27 is well under 60 days... use a date 30 days back.
    expect(serviceSoftFlagRow(makeCase({ serviceDeadlineBasisDate: '2026-06-27' }), NOW)).toBeNull()
  })

  it('flags a case in the management band after 90 days with a concrete countdown', () => {
    const row = serviceSoftFlagRow(makeCase({ serviceDeadlineBasisDate: '2026-01-01' }), NOW)
    expect(row).not.toBeNull()
    expect(row!.reason).toContain('Service deadline')
    expect(row!.age).toBeGreaterThan(60)
  })

  it('falls back to filingDate when serviceDeadlineBasisDate is missing', () => {
    const row = serviceSoftFlagRow(makeCase({ filingDate: '2026-01-01' }), NOW)
    expect(row).not.toBeNull()
  })
})

describe('staleActivityRow (rule b)', () => {
  it('skips a Resolved / Closed case entirely', () => {
    expect(staleActivityRow(makeCase({ caseStatus: 'Resolved / Closed' }), 14, NOW)).toBeNull()
  })

  it('returns null when activity is within the threshold', () => {
    expect(staleActivityRow(makeCase({ lastMeaningfulActivityDate: '2026-07-20' }), 14, NOW)).toBeNull()
  })

  it('flags a case whose activity is older than the threshold', () => {
    const row = staleActivityRow(makeCase({ lastMeaningfulActivityDate: '2026-06-01' }), 14, NOW)
    expect(row).not.toBeNull()
    expect(row!.age).toBeGreaterThan(14)
  })

  it('always flags a case with no activity date at all, never silently skipping it', () => {
    const row = staleActivityRow(makeCase({ lastMeaningfulActivityDate: null }), 14, NOW)
    expect(row).not.toBeNull()
  })

  it('computes age from dateOpened when there is no activity date, or leaves it null', () => {
    const withOpened = staleActivityRow(makeCase({ lastMeaningfulActivityDate: null, dateOpened: '2026-01-01' }), 14, NOW)
    expect(withOpened!.age).not.toBeNull()
    const withoutOpened = staleActivityRow(makeCase({ lastMeaningfulActivityDate: null, dateOpened: null }), 14, NOW)
    expect(withoutOpened!.age).toBeNull()
  })
})

describe('feeShiftReferenceRow', () => {
  it('does not flag Trial Preparation without authoritative comparison data', () => {
    expect(feeShiftReferenceRow(makeCase({ caseStatus: 'Trial Preparation', depositAmount: 100000 }))).toBeNull()
  })
})

describe('preFilingStallRow (rule 0, final implementation item 3)', () => {
  it('only applies to a case actually in Pipeline status', () => {
    expect(preFilingStallRow(makeCase({ caseStatus: 'Active Litigation' }), [], [], 7, NOW)).toBeNull()
  })

  it('returns null when there is nothing to measure (no milestones, no review notes)', () => {
    expect(preFilingStallRow(makeCase({ caseStatus: 'Pipeline' }), [], [], 7, NOW)).toBeNull()
  })

  it('returns null within the threshold', () => {
    const milestones = [makeMilestone({ markedAt: '2026-07-25T00:00:00Z' })]
    expect(preFilingStallRow(makeCase({ caseStatus: 'Pipeline' }), milestones, [], 7, NOW)).toBeNull()
  })

  it('flags a case stalled beyond the threshold, using the shared detector\'s label', () => {
    const milestones = [makeMilestone({ markedAt: '2026-07-01T00:00:00Z' })]
    const row = preFilingStallRow(makeCase({ caseStatus: 'Pipeline' }), milestones, [], 7, NOW)
    expect(row).not.toBeNull()
    expect(row!.reason).toBe('Awaiting Chief Counsel Signatures Received')
    expect(row!.ruleType).toBe('preFilingStall')
  })

  it('reflects a "sent back for revision" review note via the same shared detector', () => {
    const milestones = [makeMilestone({ markedAt: '2026-07-01T00:00:00Z' })]
    const notes: ReviewNoteRecord[] = [
      { id: 1, caseId: 1, decision: 'Sent back for revision', occurredDate: '2026-07-15', createdAt: '2026-07-15T00:00:00Z' },
    ]
    const row = preFilingStallRow(makeCase({ caseStatus: 'Pipeline' }), milestones, notes, 7, NOW)
    expect(row).not.toBeNull()
    expect(row!.reason).toBe('Returned for revision, awaiting resubmission')
  })
})

describe('buildNeedsAttentionRows', () => {
  it('allows the same case to appear more than once when it trips more than one rule', () => {
    const record = makeCase({
      id: 1,
      caseStatus: 'Trial Preparation',
      depositAmount: 50000,
      serviceDeadlineBasisDate: '2026-01-01',
      lastMeaningfulActivityDate: '2026-01-01',
    })
    const rows = buildNeedsAttentionRows([record], [], [], 14, 7, NOW)
    const caseIds = rows.map((r) => r.caseId)
    expect(caseIds.filter((id) => id === 1).length).toBeGreaterThan(1)
  })

  it('is empty when nothing trips any rule', () => {
    const record = makeCase({ id: 1, servicePerfected: true, caseStatus: 'Active Litigation', lastMeaningfulActivityDate: NOW.toISOString() })
    expect(buildNeedsAttentionRows([record], [], [], 14, 7, NOW)).toEqual([])
  })

  it('groups rows by rule type before sorting by age within each group', () => {
    const cases = [
      makeCase({ id: 1, serviceDeadlineBasisDate: '2026-01-01' }), // service, old
      makeCase({ id: 2, serviceDeadlineBasisDate: '2026-04-15' }), // service, newer but still in the management band
    ]
    const rows = buildNeedsAttentionRows(cases, [], [], 14, 7, NOW)
    expect(rows[0].caseId).toBe(1)
    expect(rows[1].caseId).toBe(2)
  })

  it('sorts the pre-filing stall group ahead of the service group, per RULE_ORDER', () => {
    const cases = [
      makeCase({ id: 1, caseStatus: 'Pipeline' }),
      makeCase({ id: 2, serviceDeadlineBasisDate: '2026-01-01' }),
    ]
    const milestones = [makeMilestone({ caseId: 1, markedAt: '2026-07-01T00:00:00Z' })]
    const rows = buildNeedsAttentionRows(cases, milestones, [], 14, 7, NOW)
    expect(rows[0].ruleType).toBe('preFilingStall')
    expect(rows[0].caseId).toBe(1)
  })
})
