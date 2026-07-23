import { describe, expect, it } from 'vitest'
import { caseloadAgeBucket, caseloadWindowMatches, isOpenCase, legalAssistantLoad } from '../App'

// Report A (Caseload & Workload) pure-logic tests. These mirror the districtForCountyChange/
// attorneyOptions precedent: pure functions extracted from the 8000+ line App.tsx specifically so
// they're unit-testable without rendering the whole component.

describe('isOpenCase', () => {
  it('treats a case with no caseStatus/status set as open (defaults to Pipeline)', () => {
    expect(isOpenCase({})).toBe(true)
    expect(isOpenCase({ caseStatus: null, status: null })).toBe(true)
  })

  it('excludes a case whose caseStatus is Resolved / Closed', () => {
    expect(isOpenCase({ caseStatus: 'Resolved / Closed', status: 'Open' })).toBe(false)
  })

  it('excludes a case whose legacy status is Closed or Complete', () => {
    expect(isOpenCase({ caseStatus: 'Active Litigation', status: 'Closed' })).toBe(false)
    expect(isOpenCase({ caseStatus: 'Active Litigation', status: 'Complete' })).toBe(false)
  })

  it('includes an open, in-progress case', () => {
    expect(isOpenCase({ caseStatus: 'Active Litigation', status: 'Open' })).toBe(true)
  })

  it('does NOT treat Triage as closed - deliberately differs from reportRows/upcomingWorkItems, which both special-case Triage as not-open', () => {
    expect(isOpenCase({ caseStatus: 'Triage', status: 'Triage' })).toBe(true)
  })
})

describe('caseloadAgeBucket', () => {
  const today = '2026-07-20'

  it('returns null when there is no dateOpened', () => {
    expect(caseloadAgeBucket(null, today)).toBeNull()
    expect(caseloadAgeBucket(undefined, today)).toBeNull()
  })

  it('buckets under90 for anything strictly less than 90 days open', () => {
    expect(caseloadAgeBucket('2026-07-01', today)).toBe('under90') // 19 days
    expect(caseloadAgeBucket('2026-04-22', today)).toBe('under90') // 89 days
  })

  it('puts exactly 90 days open in days90to180, not under90 (boundary is half-open on the low end)', () => {
    expect(caseloadAgeBucket('2026-04-21', today)).toBe('days90to180') // exactly 90 days
  })

  it('puts exactly 180 days open in days180to365, not days90to180', () => {
    // A dedicated same-DST-regime pair (both inside US daylight saving time) rather than
    // today/180-days-back, so the boundary itself is under test rather than an incidental
    // daylight-saving transition in between (this app's date-diff idiom parses ISO dates as
    // local midnight, same as the existing lifecycleDays function).
    expect(caseloadAgeBucket('2026-04-01', '2026-09-28')).toBe('days180to365')
  })

  it('puts exactly 365 days open in over365, not days180to365', () => {
    expect(caseloadAgeBucket('2025-07-20', today)).toBe('over365') // exactly 365 days
  })

  it('a case opened in the future (bad data) is treated as unbucketable rather than negative-age', () => {
    expect(caseloadAgeBucket('2026-08-01', today)).toBeNull()
  })
})

describe('caseloadWindowMatches', () => {
  const today = '2026-07-20'

  it('returns an empty array when there is no target date', () => {
    expect(caseloadWindowMatches(null, today)).toEqual([])
    expect(caseloadWindowMatches(undefined, today)).toEqual([])
  })

  it('returns an empty array for a date in the past', () => {
    expect(caseloadWindowMatches('2026-07-01', today)).toEqual([])
  })

  it('a date exactly today (0 days out) matches every window', () => {
    expect(caseloadWindowMatches(today, today)).toEqual([30, 60, 90])
  })

  it('a date well inside all three windows matches all three (cumulative, not exclusive bins)', () => {
    expect(caseloadWindowMatches('2026-07-30', today)).toEqual([30, 60, 90]) // 10 days out
  })

  it('a date exactly 30 days out counts toward the 30-day window (inclusive upper bound)', () => {
    expect(caseloadWindowMatches('2026-08-19', today)).toEqual([30, 60, 90])
  })

  it('a date 45 days out matches only the 60 and 90 day windows', () => {
    expect(caseloadWindowMatches('2026-09-03', today)).toEqual([60, 90])
  })

  it('a date beyond 90 days out matches none of the windows', () => {
    expect(caseloadWindowMatches('2026-12-01', today)).toEqual([])
  })

  // Trial density reporting passes an explicit [30, 60, 90, 120, 180] windowSizes array (see
  // caseloadTrialDensity) rather than relying on the default caseloadWindowSizes - these exercise
  // that custom-windowSizes path specifically.
  it('accepts a custom windowSizes array with more than three windows (trial density: 30/60/90/120/180)', () => {
    const windows = [30, 60, 90, 120, 180]
    expect(caseloadWindowMatches('2026-11-01', today, windows)).toEqual([120, 180]) // 104 days out
    expect(caseloadWindowMatches('2027-01-16', today, windows)).toEqual([180]) // 180 days out exactly
    expect(caseloadWindowMatches('2027-02-01', today, windows)).toEqual([]) // beyond every window
  })
})

describe('legalAssistantLoad', () => {
  const legalAssistants = [
    { id: 1, name: 'Pat Rivera', isActive: true, sortOrder: 1, attorneyIds: [10], attorneyNames: ['Michelle Davenport'] },
    { id: 2, name: 'Sam Okafor', isActive: true, sortOrder: 2, attorneyIds: [11, 12], attorneyNames: ['Angela Dodson', 'Helen Newberry'] },
    { id: 3, name: 'Retired LA', isActive: false, sortOrder: 3, attorneyIds: [10], attorneyNames: ['Michelle Davenport'] },
  ]

  it('counts open cases per LA via their tied attorneys, ignoring closed cases', () => {
    const cases = [
      { assignedAttorney: 'Michelle Davenport', caseStatus: 'Active Litigation', status: 'Open' },
      { assignedAttorney: 'Michelle Davenport', caseStatus: 'Resolved / Closed', status: 'Open' }, // closed, excluded
      { assignedAttorney: 'Angela Dodson', caseStatus: 'Pipeline', status: 'Open' },
      { assignedAttorney: 'Helen Newberry', caseStatus: 'Trial Preparation', status: 'Open' },
    ]
    const result = legalAssistantLoad(cases, legalAssistants)
    expect(result).toEqual([
      { id: 1, name: 'Pat Rivera', openCaseCount: 1 },
      { id: 2, name: 'Sam Okafor', openCaseCount: 2 },
    ])
  })

  it('excludes inactive legal assistants entirely, even if their tied attorney has open cases', () => {
    const cases = [{ assignedAttorney: 'Michelle Davenport', caseStatus: 'Active Litigation', status: 'Open' }]
    const result = legalAssistantLoad(cases, legalAssistants)
    expect(result.find((row) => row.name === 'Retired LA')).toBeUndefined()
  })

  it('ignores attorneys with no tied LA (e.g. Chief/Deputy Chief Counsel) - their cases contribute to no row', () => {
    const cases = [
      { assignedAttorney: 'Chief Counsel', caseStatus: 'Active Litigation', status: 'Open' },
      { assignedAttorney: 'Michelle Davenport', caseStatus: 'Active Litigation', status: 'Open' },
    ]
    const result = legalAssistantLoad(cases, legalAssistants)
    const total = result.reduce((sum, row) => sum + row.openCaseCount, 0)
    expect(total).toBe(1)
  })

  it('sums correctly when one LA supports multiple attorneys with different open-case counts', () => {
    const cases = [
      { assignedAttorney: 'Angela Dodson', caseStatus: 'Pipeline', status: 'Open' },
      { assignedAttorney: 'Angela Dodson', caseStatus: 'Active Litigation', status: 'Open' },
      { assignedAttorney: 'Angela Dodson', caseStatus: 'Settlement Pending', status: 'Open' },
      { assignedAttorney: 'Helen Newberry', caseStatus: 'Trial Preparation', status: 'Open' },
    ]
    const result = legalAssistantLoad(cases, legalAssistants)
    expect(result.find((row) => row.name === 'Sam Okafor')?.openCaseCount).toBe(4)
  })

  it('returns a zero count for an active LA with no open cases among their tied attorneys', () => {
    const result = legalAssistantLoad([], legalAssistants)
    expect(result).toEqual([
      { id: 1, name: 'Pat Rivera', openCaseCount: 0 },
      { id: 2, name: 'Sam Okafor', openCaseCount: 0 },
    ])
  })
})
