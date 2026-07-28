import { describe, expect, it } from 'vitest'
import type { CaseRecord } from '../../App'
import {
  buildNeedsAttentionRows,
  feeShiftReferenceRow,
  pendingApprovalRow,
  serviceSoftFlagRow,
  staleActivityRow,
} from '../NeedsAttentionTab'
import type { SettlementAuthorityRequestRecord } from '../types'

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

  it('flags a case past the 60-day window, using soft-flag language (never "overdue")', () => {
    const row = serviceSoftFlagRow(makeCase({ serviceDeadlineBasisDate: '2026-01-01' }), NOW)
    expect(row).not.toBeNull()
    expect(row!.reason).toBe('Service pending beyond the 60-day check-in point')
    expect(row!.reason.toLowerCase()).not.toContain('overdue')
    expect(row!.reason.toLowerCase()).not.toContain('missed')
    expect(row!.reason.toLowerCase()).not.toContain('violation')
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

describe('feeShiftReferenceRow (rule c)', () => {
  it('only applies to Trial Preparation cases', () => {
    expect(feeShiftReferenceRow(makeCase({ caseStatus: 'Active Litigation', depositAmount: 100000 }))).toBeNull()
  })

  it('returns null when there is no deposit amount', () => {
    expect(feeShiftReferenceRow(makeCase({ caseStatus: 'Trial Preparation', depositAmount: null }))).toBeNull()
  })

  it('computes the deposit-plus-20% figure and frames it as a forward-looking reference only', () => {
    const row = feeShiftReferenceRow(makeCase({ caseStatus: 'Trial Preparation', depositAmount: 100000 }))
    expect(row).not.toBeNull()
    expect(row!.age).toBeNull()
    expect(row!.reason).toContain('$100,000.00')
    expect(row!.reason).toContain('$120,000.00')
    expect(row!.reason.toLowerCase()).not.toContain('verdict has')
    expect(row!.reason.toLowerCase()).not.toContain('fees are owed')
  })
})

describe('pendingApprovalRow (rule d)', () => {
  function makeRequest(overrides: Partial<SettlementAuthorityRequestRecord> = {}): SettlementAuthorityRequestRecord {
    return { id: 1, caseId: 1, requestedAmount: 50000, status: 'Pending', requestedAt: '2026-07-01T00:00:00Z', ...overrides }
  }

  it('ignores a non-Pending request', () => {
    expect(pendingApprovalRow(makeRequest({ status: 'Approved' }), makeCase(), 5, NOW)).toBeNull()
  })

  it('returns null within the threshold', () => {
    expect(pendingApprovalRow(makeRequest({ requestedAt: '2026-07-25T00:00:00Z' }), makeCase(), 5, NOW)).toBeNull()
  })

  it('flags a request pending beyond the threshold, falling back to the case attorney', () => {
    const row = pendingApprovalRow(makeRequest({ requestingAttorney: null }), makeCase({ assignedAttorney: 'Jane Roe' }), 5, NOW)
    expect(row).not.toBeNull()
    expect(row!.attorney).toBe('Jane Roe')
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
    const rows = buildNeedsAttentionRows([record], [], 14, 5, NOW)
    const caseIds = rows.map((r) => r.caseId)
    expect(caseIds.filter((id) => id === 1).length).toBeGreaterThan(1)
  })

  it('is empty when nothing trips any rule', () => {
    const record = makeCase({ id: 1, servicePerfected: true, caseStatus: 'Active Litigation', lastMeaningfulActivityDate: NOW.toISOString() })
    expect(buildNeedsAttentionRows([record], [], 14, 5, NOW)).toEqual([])
  })

  it('groups rows by rule type before sorting by age within each group', () => {
    const cases = [
      makeCase({ id: 1, serviceDeadlineBasisDate: '2026-01-01' }), // service, old
      makeCase({ id: 2, serviceDeadlineBasisDate: '2026-05-01' }), // service, newer
    ]
    const rows = buildNeedsAttentionRows(cases, [], 14, 5, NOW)
    expect(rows[0].caseId).toBe(1)
    expect(rows[1].caseId).toBe(2)
  })
})
