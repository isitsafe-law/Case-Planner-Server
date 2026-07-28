import { describe, expect, it } from 'vitest'
import type { CaseRecord } from '../../App'
import { buildJobRows, sortJobRows } from '../ByJobTab'

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

describe('buildJobRows', () => {
  it('groups a blank job number into the "No Job Number" bucket, not an error', () => {
    const rows = buildJobRows([makeCase({ id: 1, jobNumber: '' })], [])
    expect(rows).toHaveLength(1)
    expect(rows[0].jobNumber).toBe('No Job Number')
  })

  it('sums depositAmount treating null as 0, so an all-null job correctly totals $0', () => {
    const rows = buildJobRows([makeCase({ id: 1, depositAmount: null }), makeCase({ id: 2, jobNumber: 'JOB1', depositAmount: null })], [])
    expect(rows[0].totalDeposit).toBe(0)
  })

  it('sums depositAmount across a job\'s tracts', () => {
    const rows = buildJobRows(
      [makeCase({ id: 1, jobNumber: 'JOB1', depositAmount: 100000 }), makeCase({ id: 2, jobNumber: 'JOB1', depositAmount: 50000 })],
      [],
    )
    expect(rows[0].totalDeposit).toBe(150000)
  })

  it('collects distinct assignedAttorney values, alphabetized', () => {
    const rows = buildJobRows(
      [
        makeCase({ id: 1, jobNumber: 'JOB1', assignedAttorney: 'John Smith' }),
        makeCase({ id: 2, jobNumber: 'JOB1', assignedAttorney: 'Jane Roe' }),
        makeCase({ id: 3, jobNumber: 'JOB1', assignedAttorney: 'Jane Roe' }),
      ],
      [],
    )
    expect(rows[0].attorneys).toEqual(['Jane Roe', 'John Smith'])
  })
})

describe('sortJobRows', () => {
  it('sorts jobNumber numerically-aware (JOB2 before JOB10)', () => {
    const rows = buildJobRows(
      [makeCase({ id: 1, jobNumber: 'JOB10' }), makeCase({ id: 2, jobNumber: 'JOB2' })],
      [],
    )
    const sorted = sortJobRows(rows, 'jobNumber', 'asc')
    expect(sorted.map((r) => r.jobNumber)).toEqual(['JOB2', 'JOB10'])
  })

  it('sorts by total deposit', () => {
    const rows = buildJobRows(
      [makeCase({ id: 1, jobNumber: 'JOB1', depositAmount: 100 }), makeCase({ id: 2, jobNumber: 'JOB2', depositAmount: 500 })],
      [],
    )
    const desc = sortJobRows(rows, 'totalDeposit', 'desc')
    expect(desc[0].jobNumber).toBe('JOB2')
  })
})
