import { describe, expect, it } from 'vitest'
import type { CaseRecord, Hearing } from '../../App'
import { bucketCaseStatus, nextHardDate, statusDistribution } from '../dashboardAggregation'

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

describe('bucketCaseStatus', () => {
  it('defaults a blank/missing caseStatus to Pipeline', () => {
    expect(bucketCaseStatus(makeCase({ caseStatus: null }))).toBe('Pipeline')
    expect(bucketCaseStatus(makeCase({ caseStatus: undefined }))).toBe('Pipeline')
  })

  it('passes through each of the six canonical values unchanged', () => {
    expect(bucketCaseStatus(makeCase({ caseStatus: 'Active Litigation' }))).toBe('Active Litigation')
    expect(bucketCaseStatus(makeCase({ caseStatus: 'Resolved / Closed' }))).toBe('Resolved / Closed')
  })

  it('folds an unrecognized value (e.g. legacy Triage) into Pipeline rather than dropping it', () => {
    expect(bucketCaseStatus(makeCase({ caseStatus: 'Triage' }))).toBe('Pipeline')
    expect(bucketCaseStatus(makeCase({ caseStatus: 'SomeUnknownValue' }))).toBe('Pipeline')
  })
})

describe('statusDistribution', () => {
  it('returns all six buckets, including zero counts, in lifecycle order', () => {
    const result = statusDistribution([])
    expect(result.map((r) => r.status)).toEqual([
      'Pipeline',
      'Filed / Service Pending',
      'Active Litigation',
      'Settlement Pending',
      'Trial Preparation',
      'Resolved / Closed',
    ])
    expect(result.every((r) => r.count === 0)).toBe(true)
  })

  it('counts cases into their bucket', () => {
    const cases = [
      makeCase({ id: 1, caseStatus: 'Pipeline' }),
      makeCase({ id: 2, caseStatus: 'Pipeline' }),
      makeCase({ id: 3, caseStatus: 'Active Litigation' }),
      makeCase({ id: 4, caseStatus: 'Resolved / Closed' }),
    ]
    const result = statusDistribution(cases)
    const byStatus = Object.fromEntries(result.map((r) => [r.status, r.count]))
    expect(byStatus['Pipeline']).toBe(2)
    expect(byStatus['Active Litigation']).toBe(1)
    expect(byStatus['Resolved / Closed']).toBe(1)
    expect(byStatus['Filed / Service Pending']).toBe(0)
  })
})

describe('nextHardDate', () => {
  it('returns null when no case has a deadline, hearing, or trial date', () => {
    expect(nextHardDate([makeCase()], [])).toBeNull()
  })

  it('picks the earliest of nextDeadlineDate, hearingDate, and trialDate', () => {
    const cases = [makeCase({ id: 1, nextDeadlineDate: '2026-08-15', nextDeadlineTitle: 'File Answer' })]
    const hearings: Hearing[] = [
      { id: 1, caseId: 1, title: 'Status Conference', hearingDate: '2026-08-01', createdAt: '', updatedAt: '' },
    ]
    const result = nextHardDate(cases, hearings)
    expect(result).toEqual({ date: '2026-08-01', label: 'Status Conference' })
  })

  it('labels a trialDate candidate with the correct ARDOT term, never generic "Trial"', () => {
    const cases = [makeCase({ id: 1, trialDate: '2026-09-01' })]
    const result = nextHardDate(cases, [])
    expect(result).toEqual({ date: '2026-09-01', label: 'Jury Trial on Just Compensation' })
  })

  it('only considers hearings belonging to one of the passed-in cases', () => {
    const cases = [makeCase({ id: 1 })]
    const hearings: Hearing[] = [
      { id: 1, caseId: 999, title: 'Unrelated Hearing', hearingDate: '2026-08-01', createdAt: '', updatedAt: '' },
    ]
    expect(nextHardDate(cases, hearings)).toBeNull()
  })

  it('falls back to eventType, then "Hearing", when a hearing has no title', () => {
    const cases = [makeCase({ id: 1 })]
    const hearings: Hearing[] = [
      { id: 1, caseId: 1, title: '', eventType: 'Status Conference', hearingDate: '2026-08-01', createdAt: '', updatedAt: '' },
    ]
    expect(nextHardDate(cases, hearings)?.label).toBe('Status Conference')
  })
})
