import { describe, expect, it } from 'vitest'
import type { CaseRecord } from '../../App'
import { buildAttorneyRows, sortAttorneyRows } from '../ByAttorneyTab'
import type { SettlementAuthorityRequestRecord } from '../types'

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
    ...overrides,
  }
}

describe('buildAttorneyRows', () => {
  it('groups blank/missing assignedAttorney into "Unassigned"', () => {
    const rows = buildAttorneyRows(
      [makeCase({ id: 1, assignedAttorney: null }), makeCase({ id: 2, assignedAttorney: '' })],
      [],
      [],
    )
    expect(rows).toHaveLength(1)
    expect(rows[0].attorney).toBe('Unassigned')
    expect(rows[0].totalTracts).toBe(2)
  })

  it('joins pending Settlement Authority requests to the matching attorney via caseId', () => {
    const cases = [makeCase({ id: 1, assignedAttorney: 'Jane Roe' }), makeCase({ id: 2, assignedAttorney: 'John Smith' })]
    const requests: SettlementAuthorityRequestRecord[] = [
      { id: 1, caseId: 1, requestedAmount: 1000, status: 'Pending', requestedAt: '2026-07-01T00:00:00Z' },
      { id: 2, caseId: 2, requestedAmount: 1000, status: 'Approved', requestedAt: '2026-07-01T00:00:00Z' },
    ]
    const rows = buildAttorneyRows(cases, [], requests)
    const jane = rows.find((r) => r.attorney === 'Jane Roe')
    const john = rows.find((r) => r.attorney === 'John Smith')
    expect(jane?.pendingApprovalsCount).toBe(1)
    expect(john?.pendingApprovalsCount).toBe(0)
  })
})

describe('sortAttorneyRows', () => {
  it('sorts undated next-hard-date rows last regardless of direction', () => {
    const rows = buildAttorneyRows(
      [
        makeCase({ id: 1, assignedAttorney: 'A', nextDeadlineDate: '2026-08-01', nextDeadlineTitle: 'Deadline' }),
        makeCase({ id: 2, assignedAttorney: 'B' }),
      ],
      [],
      [],
    )
    const asc = sortAttorneyRows(rows, 'nextHardDate', 'asc')
    const desc = sortAttorneyRows(rows, 'nextHardDate', 'desc')
    expect(asc[asc.length - 1].attorney).toBe('B')
    expect(desc[desc.length - 1].attorney).toBe('B')
  })

  it('toggles ascending/descending by tract count', () => {
    const rows = buildAttorneyRows(
      [makeCase({ id: 1, assignedAttorney: 'A' }), makeCase({ id: 2, assignedAttorney: 'B' }), makeCase({ id: 3, assignedAttorney: 'B' })],
      [],
      [],
    )
    const asc = sortAttorneyRows(rows, 'tracts', 'asc')
    expect(asc[0].attorney).toBe('A')
    const desc = sortAttorneyRows(rows, 'tracts', 'desc')
    expect(desc[0].attorney).toBe('B')
  })
})
