import { describe, expect, it } from 'vitest'
import {
  aggregateOutcomes,
  aggregateOutcomesByAttorney,
  isOutcomeEligible,
  outcomeClosePeriod,
  outcomeDelta,
  outcomeRatio,
} from '../App'

// Report B (Just-Compensation / Outcome Reporting) pure-logic tests. Mirrors the Report A
// (caseloadReporting.test.ts) precedent: pure functions extracted from the 8000+ line App.tsx
// specifically so they're unit-testable without rendering the whole component.

describe('isOutcomeEligible', () => {
  it('excludes an open case even with complete deposit + final judgment data', () => {
    expect(isOutcomeEligible({ caseStatus: 'Active Litigation', status: 'Open', depositAmount: 100000, finalJudgmentAmount: 120000 })).toBe(false)
  })

  it('excludes a closed case with a zero deposit', () => {
    expect(isOutcomeEligible({ caseStatus: 'Resolved / Closed', depositAmount: 0, finalJudgmentAmount: 120000 })).toBe(false)
  })

  it('excludes a closed case with a missing (null/undefined) deposit', () => {
    expect(isOutcomeEligible({ caseStatus: 'Resolved / Closed', depositAmount: null, finalJudgmentAmount: 120000 })).toBe(false)
    expect(isOutcomeEligible({ caseStatus: 'Resolved / Closed', finalJudgmentAmount: 120000 })).toBe(false)
  })

  it('excludes a closed case with a missing final judgment amount', () => {
    expect(isOutcomeEligible({ caseStatus: 'Resolved / Closed', depositAmount: 100000, finalJudgmentAmount: null })).toBe(false)
    expect(isOutcomeEligible({ caseStatus: 'Resolved / Closed', depositAmount: 100000 })).toBe(false)
  })

  it('includes a fully-qualified closed case (closed, positive deposit, present final judgment)', () => {
    expect(isOutcomeEligible({ caseStatus: 'Resolved / Closed', depositAmount: 100000, finalJudgmentAmount: 120000 })).toBe(true)
  })

  it('a legacy-status closed case (status Closed/Complete, no caseStatus) is still eligible', () => {
    expect(isOutcomeEligible({ status: 'Closed', depositAmount: 50000, finalJudgmentAmount: 45000 })).toBe(true)
  })
})

describe('outcomeDelta / outcomeRatio', () => {
  it('computes a positive delta and a >1.0 ratio when final judgment exceeds deposit', () => {
    const record = { depositAmount: 100000, finalJudgmentAmount: 150000 }
    expect(outcomeDelta(record)).toBe(50000)
    expect(outcomeRatio(record)).toBeCloseTo(1.5)
  })

  it('computes a negative delta and a sub-1.0 ratio when final judgment is less than deposit', () => {
    const record = { depositAmount: 100000, finalJudgmentAmount: 80000 }
    expect(outcomeDelta(record)).toBe(-20000)
    expect(outcomeRatio(record)).toBeCloseTo(0.8)
  })

  it('a final judgment exactly equal to deposit gives a zero delta and a 1.0 ratio', () => {
    const record = { depositAmount: 100000, finalJudgmentAmount: 100000 }
    expect(outcomeDelta(record)).toBe(0)
    expect(outcomeRatio(record)).toBe(1)
  })
})

describe('outcomeClosePeriod', () => {
  it('returns null for a missing or unparseable date', () => {
    expect(outcomeClosePeriod(null, 'quarter')).toBeNull()
    expect(outcomeClosePeriod(undefined, 'year')).toBeNull()
    expect(outcomeClosePeriod('not-a-date', 'quarter')).toBeNull()
  })

  it('labels the year correctly regardless of granularity', () => {
    expect(outcomeClosePeriod('2026-03-15', 'year')).toBe('2026')
    expect(outcomeClosePeriod('2026-04-01', 'year')).toBe('2026')
  })

  it('a March close and an April close land in different quarters (Q1 vs Q2 boundary)', () => {
    expect(outcomeClosePeriod('2026-03-31', 'quarter')).toBe('2026-Q1')
    expect(outcomeClosePeriod('2026-04-01', 'quarter')).toBe('2026-Q2')
  })

  it('covers all four quarter boundaries within one year', () => {
    expect(outcomeClosePeriod('2026-01-01', 'quarter')).toBe('2026-Q1')
    expect(outcomeClosePeriod('2026-06-30', 'quarter')).toBe('2026-Q2')
    expect(outcomeClosePeriod('2026-09-30', 'quarter')).toBe('2026-Q3')
    expect(outcomeClosePeriod('2026-12-31', 'quarter')).toBe('2026-Q4')
  })
})

describe('aggregateOutcomes', () => {
  const records = [
    { key: 'Jury Trial', depositAmount: 100000, finalJudgmentAmount: 150000 },
    { key: 'Jury Trial', depositAmount: 200000, finalJudgmentAmount: 260000 },
    { key: 'Settlement', depositAmount: 50000, finalJudgmentAmount: 45000 },
    { key: null, depositAmount: 10000, finalJudgmentAmount: 9000 },
    { key: '', depositAmount: 20000, finalJudgmentAmount: 21000 },
  ]

  it('groups by keyFn and computes correct per-group averages', () => {
    const result = aggregateOutcomes(records, (record) => record.key)
    const juryTrial = result.find((row) => row.key === 'Jury Trial')!
    expect(juryTrial.count).toBe(2)
    expect(juryTrial.avgDeposit).toBe(150000) // (100000+200000)/2
    expect(juryTrial.avgFinal).toBe(205000) // (150000+260000)/2
    expect(juryTrial.avgDelta).toBe(55000) // (50000+60000)/2
    expect(juryTrial.avgRatio).toBeCloseTo(1.4) // (1.5+1.3)/2

    const settlement = result.find((row) => row.key === 'Settlement')!
    expect(settlement.count).toBe(1)
    expect(settlement.avgDelta).toBe(-5000)
  })

  it('excludes any record whose keyFn returns null or empty from every group', () => {
    const result = aggregateOutcomes(records, (record) => record.key)
    const totalGrouped = result.reduce((sum, row) => sum + row.count, 0)
    expect(totalGrouped).toBe(3) // the null-key and empty-string-key records are dropped
    expect(result.find((row) => row.key === '')).toBeUndefined()
  })
})

describe('aggregateOutcomesByAttorney', () => {
  const records = [
    { assignedAttorney: 'Michelle Davenport', depositAmount: 100000, finalJudgmentAmount: 150000, takingType: 'Partial' },
    { assignedAttorney: 'Michelle Davenport', depositAmount: 300000, finalJudgmentAmount: 340000, takingType: 'Full' },
    { assignedAttorney: 'Michelle Davenport', depositAmount: 50000, finalJudgmentAmount: 55000, takingType: 'Partial' },
    { assignedAttorney: 'Angela Dodson', depositAmount: 200000, finalJudgmentAmount: 180000, takingType: 'TCE' },
  ]

  it('computes correct taking-type mix counts per attorney', () => {
    const result = aggregateOutcomesByAttorney(records)
    const davenport = result.find((row) => row.key === 'Michelle Davenport')!
    expect(davenport.takingTypeMix).toEqual({ Partial: 2, Full: 1, TCE: 0 })

    const dodson = result.find((row) => row.key === 'Angela Dodson')!
    expect(dodson.takingTypeMix).toEqual({ Partial: 0, Full: 0, TCE: 1 })
  })

  it('computes the correct min/max deposit range per attorney', () => {
    const result = aggregateOutcomesByAttorney(records)
    const davenport = result.find((row) => row.key === 'Michelle Davenport')!
    expect(davenport.depositRange).toEqual({ min: 50000, max: 300000 })
  })

  it('an attorney with zero eligible cases is legitimately absent, not an all-zero row - callers ' +
     'are expected to pass only outcome-eligible records in (unlike the generic disposition/taking-' +
     'type breakdowns, there is no small fixed enumeration of attorneys worth zero-padding, so ' +
     'absence from the result means "no eligible cases", not "data missing")', () => {
    const result = aggregateOutcomesByAttorney(records)
    expect(result.find((row) => row.key === 'Someone With No Cases')).toBeUndefined()
    expect(result.every((row) => row.count > 0)).toBe(true)
  })

  it('groups a blank assignedAttorney under its own "Unassigned" bucket rather than dropping it', () => {
    const result = aggregateOutcomesByAttorney([
      ...records,
      { assignedAttorney: '', depositAmount: 10000, finalJudgmentAmount: 12000, takingType: 'Partial' },
      { assignedAttorney: null, depositAmount: 20000, finalJudgmentAmount: 18000, takingType: 'Full' },
    ])
    const unassigned = result.find((row) => row.key === 'Unassigned')!
    expect(unassigned).toBeDefined()
    expect(unassigned.count).toBe(2)
  })
})
