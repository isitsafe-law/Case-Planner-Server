import { describe, expect, it } from 'vitest'
import type { CaseRecord } from '../../App'
import { buildAttorneyRows, sortAttorneyRows } from '../ByAttorneyTab'

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
    )
    expect(rows).toHaveLength(1)
    expect(rows[0].attorney).toBe('Unassigned')
    expect(rows[0].totalTracts).toBe(2)
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
    )
    const asc = sortAttorneyRows(rows, 'tracts', 'asc')
    expect(asc[0].attorney).toBe('A')
    const desc = sortAttorneyRows(rows, 'tracts', 'desc')
    expect(desc[0].attorney).toBe('B')
  })
})
