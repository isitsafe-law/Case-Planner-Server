import { describe, expect, it } from 'vitest'
import { daysPending, settlementAuthorityDelta, settlementAuthorityDeltaPercent, sortSettlementAuthorityRows } from '../SettlementAuthoritySection'
import type { SettlementAuthorityRequestRecord } from '../types'
import type { CaseRecord } from '../../App'

function makeRequest(overrides: Partial<SettlementAuthorityRequestRecord> = {}): SettlementAuthorityRequestRecord {
  return {
    id: 1,
    caseId: 1,
    requestedAmount: 50_000,
    status: 'Pending',
    requestedAt: '2026-07-01T00:00:00Z',
    ...overrides,
  }
}

function makeCase(overrides: Partial<CaseRecord> = {}): CaseRecord {
  return { id: 1, caseName: 'Fixture Case', jobNumber: '', tract: '', ...overrides } as CaseRecord
}

describe('settlementAuthorityDelta', () => {
  it('computes requested minus the Estimate of Just Compensation deposit', () => {
    expect(settlementAuthorityDelta(150000, 100000)).toBe(50000)
  })

  it('returns null when there is no deposit amount to compare against (null, not zero-divide)', () => {
    expect(settlementAuthorityDelta(150000, null)).toBeNull()
    expect(settlementAuthorityDelta(150000, undefined)).toBeNull()
  })

  it('allows a zero deposit amount to still compute a delta (only null/undefined short-circuits)', () => {
    expect(settlementAuthorityDelta(150000, 0)).toBe(150000)
  })
})

describe('settlementAuthorityDeltaPercent', () => {
  it('computes delta as a percentage of the deposit amount', () => {
    expect(settlementAuthorityDeltaPercent(150000, 100000)).toBe(50)
  })

  it('returns null for a null, undefined, or zero deposit amount (avoids divide-by-zero/NaN)', () => {
    expect(settlementAuthorityDeltaPercent(150000, null)).toBeNull()
    expect(settlementAuthorityDeltaPercent(150000, undefined)).toBeNull()
    expect(settlementAuthorityDeltaPercent(150000, 0)).toBeNull()
  })

  it('supports a negative delta (requested less than the deposit)', () => {
    expect(settlementAuthorityDeltaPercent(80000, 100000)).toBe(-20)
  })
})

describe('daysPending', () => {
  it('floors whole days between requestedAt and now', () => {
    const now = new Date('2026-07-27T12:00:00Z')
    expect(daysPending('2026-07-20T12:00:00Z', now)).toBe(7)
    expect(daysPending('2026-07-27T06:00:00Z', now)).toBe(0)
  })

  it('never returns a negative count for a future-dated requestedAt', () => {
    const now = new Date('2026-07-27T12:00:00Z')
    expect(daysPending('2026-07-28T12:00:00Z', now)).toBe(0)
  })
})

// Manager Dashboard sign-off consolidation, item 4: the Approvals tab's Settlement Authority
// section is a sortable log now, not a decision inbox gated to one role - covers the sort function
// directly, mirroring ByAttorneyTab.test.tsx's coverage of sortAttorneyRows.
describe('sortSettlementAuthorityRows', () => {
  it('sorts by requestedAmount ascending/descending', () => {
    const rows = [
      { request: makeRequest({ id: 1, requestedAmount: 60_000 }), matchedCase: makeCase() },
      { request: makeRequest({ id: 2, requestedAmount: 30_000 }), matchedCase: makeCase() },
    ]
    expect(sortSettlementAuthorityRows(rows, 'requestedAmount', 'asc').map((r) => r.request.id)).toEqual([2, 1])
    expect(sortSettlementAuthorityRows(rows, 'requestedAmount', 'desc').map((r) => r.request.id)).toEqual([1, 2])
  })

  it('sorts by status alphabetically', () => {
    const rows = [
      { request: makeRequest({ id: 1, status: 'Pending' }), matchedCase: makeCase() },
      { request: makeRequest({ id: 2, status: 'Denied' }), matchedCase: makeCase() },
    ]
    expect(sortSettlementAuthorityRows(rows, 'status', 'asc').map((r) => r.request.id)).toEqual([2, 1])
  })

  it('sorts by decidedAt with undated (never-decided) rows always last regardless of direction', () => {
    const rows = [
      { request: makeRequest({ id: 1, decidedAt: '2026-07-10T00:00:00Z' }), matchedCase: makeCase() },
      { request: makeRequest({ id: 2, decidedAt: undefined }), matchedCase: makeCase() },
      { request: makeRequest({ id: 3, decidedAt: '2026-07-01T00:00:00Z' }), matchedCase: makeCase() },
    ]
    expect(sortSettlementAuthorityRows(rows, 'decidedAt', 'asc').map((r) => r.request.id)).toEqual([3, 1, 2])
    expect(sortSettlementAuthorityRows(rows, 'decidedAt', 'desc').map((r) => r.request.id)).toEqual([1, 3, 2])
  })

  it('sorts by jobTract using the joined case', () => {
    const rows = [
      { request: makeRequest({ id: 1 }), matchedCase: makeCase({ jobNumber: 'B', tract: '2' }) },
      { request: makeRequest({ id: 2 }), matchedCase: makeCase({ jobNumber: 'A', tract: '1' }) },
    ]
    expect(sortSettlementAuthorityRows(rows, 'jobTract', 'asc').map((r) => r.request.id)).toEqual([2, 1])
  })
})
