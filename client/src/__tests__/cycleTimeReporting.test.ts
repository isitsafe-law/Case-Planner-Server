import { describe, expect, it } from 'vitest'
import {
  aggregateDurations,
  computeHandoffSegments,
  isResolutionEligible,
  resolutionDays,
} from '../App'
import type { PipelineHandoffRecord } from '../dashboard/types'

// Report C (Cycle-Time Reporting) pure-logic tests. Mirrors the Report A (caseloadReporting.test.ts)
// / Report B (outcomeReporting.test.ts) precedent: pure functions extracted from the 8000+ line
// App.tsx specifically so they're unit-testable without rendering the whole component.

function handoff(overrides: Partial<PipelineHandoffRecord> & { id: number; caseId: number }): PipelineHandoffRecord {
  return {
    previousHolder: null,
    newHolder: '',
    previousStage: null,
    newStage: '',
    handoffDate: null,
    nextReviewDate: null,
    note: null,
    createdAt: null,
    ...overrides,
  }
}

describe('isResolutionEligible', () => {
  it('excludes an open case even with both filing and closed dates set', () => {
    expect(isResolutionEligible({ caseStatus: 'Active Litigation', status: 'Open', filingDate: '2026-01-01', closedDate: '2026-06-01' })).toBe(false)
  })

  it('excludes a closed case missing a filing date', () => {
    expect(isResolutionEligible({ caseStatus: 'Resolved / Closed', filingDate: null, closedDate: '2026-06-01' })).toBe(false)
    expect(isResolutionEligible({ caseStatus: 'Resolved / Closed', closedDate: '2026-06-01' })).toBe(false)
  })

  it('excludes a closed case missing a closed date', () => {
    expect(isResolutionEligible({ caseStatus: 'Resolved / Closed', filingDate: '2026-01-01', closedDate: null })).toBe(false)
    expect(isResolutionEligible({ caseStatus: 'Resolved / Closed', filingDate: '2026-01-01' })).toBe(false)
  })

  it('includes a fully-qualified closed case (closed, filing date, closed date both present)', () => {
    expect(isResolutionEligible({ caseStatus: 'Resolved / Closed', filingDate: '2026-01-01', closedDate: '2026-06-01' })).toBe(true)
  })

  it('a legacy-status closed case (status Closed/Complete, no caseStatus) is still eligible', () => {
    expect(isResolutionEligible({ status: 'Closed', filingDate: '2026-01-01', closedDate: '2026-03-01' })).toBe(true)
  })
})

describe('resolutionDays', () => {
  it('computes correct day math from filing to close', () => {
    expect(resolutionDays({ filingDate: '2026-01-01', closedDate: '2026-01-31' })).toBe(30)
    expect(resolutionDays({ filingDate: '2026-01-01', closedDate: '2026-01-01' })).toBe(0)
  })

  it('returns null (rather than a negative number) when closedDate precedes filingDate - bad data', () => {
    expect(resolutionDays({ filingDate: '2026-06-01', closedDate: '2026-01-01' })).toBeNull()
  })
})

describe('aggregateDurations', () => {
  const items = [
    { key: 'Discovery & Evaluation', days: 10 },
    { key: 'Discovery & Evaluation', days: 30 },
    { key: 'Trial Track', days: 5 },
    { key: null, days: 100 },
    { key: '', days: 200 },
  ]

  it('groups by keyFn and computes correct per-group count/average', () => {
    const result = aggregateDurations(items, (item) => item.key)
    const discovery = result.find((row) => row.key === 'Discovery & Evaluation')!
    expect(discovery.count).toBe(2)
    expect(discovery.avgDays).toBe(20) // (10+30)/2

    const trial = result.find((row) => row.key === 'Trial Track')!
    expect(trial.count).toBe(1)
    expect(trial.avgDays).toBe(5)
  })

  it('excludes any item whose keyFn returns null or empty from every group', () => {
    const result = aggregateDurations(items, (item) => item.key)
    const totalGrouped = result.reduce((sum, row) => sum + row.count, 0)
    expect(totalGrouped).toBe(3)
    expect(result.find((row) => row.key === '')).toBeUndefined()
  })
})

describe('computeHandoffSegments', () => {
  it('a multi-transition single case: each segment has correct days/fromStage/toStage/fromHolder/toHolder, and the FIRST segment anchors off dateOpened (not some other date)', () => {
    const handoffs = [
      handoff({ id: 1, caseId: 1, previousHolder: 'Legal Assistant', newHolder: 'Attorney', previousStage: 'Intake & Filing', newStage: 'Service', handoffDate: '2026-01-20' }),
      handoff({ id: 2, caseId: 1, previousHolder: 'Attorney', newHolder: 'Paralegal', previousStage: 'Service', newStage: 'Discovery & Evaluation', handoffDate: '2026-02-10' }),
    ]
    const dateOpenedByCaseId = new Map<number, string | null>([[1, '2026-01-01']])

    const segments = computeHandoffSegments(handoffs, dateOpenedByCaseId)

    expect(segments).toHaveLength(2)
    // First segment: dateOpened (2026-01-01) -> first handoff date (2026-01-20) = 19 days.
    expect(segments[0]).toEqual({
      caseId: 1,
      fromStage: 'Intake & Filing',
      toStage: 'Service',
      fromHolder: 'Legal Assistant',
      toHolder: 'Attorney',
      days: 19,
    })
    // Second segment: first handoff date (2026-01-20) -> second handoff date (2026-02-10) = 21 days.
    expect(segments[1]).toEqual({
      caseId: 1,
      fromStage: 'Service',
      toStage: 'Discovery & Evaluation',
      fromHolder: 'Attorney',
      toHolder: 'Paralegal',
      days: 21,
    })
  })

  it('a case with only one handoff ever produces exactly one segment, anchored off dateOpened', () => {
    const handoffs = [
      handoff({ id: 1, caseId: 2, previousHolder: 'Legal Assistant', newHolder: 'Attorney', previousStage: 'Intake & Filing', newStage: 'Service', handoffDate: '2026-01-15' }),
    ]
    const dateOpenedByCaseId = new Map<number, string | null>([[2, '2026-01-01']])

    const segments = computeHandoffSegments(handoffs, dateOpenedByCaseId)

    expect(segments).toHaveLength(1)
    expect(segments[0].days).toBe(14)
  })

  it('a case with zero handoffs contributes nothing', () => {
    const dateOpenedByCaseId = new Map<number, string | null>([[3, '2026-01-01']])
    const segments = computeHandoffSegments([], dateOpenedByCaseId)
    expect(segments).toHaveLength(0)
  })

  it('multiple interleaved cases do not leak segments across case boundaries', () => {
    const handoffs = [
      handoff({ id: 1, caseId: 10, previousStage: 'Intake & Filing', newStage: 'Service', handoffDate: '2026-01-15' }),
      handoff({ id: 2, caseId: 20, previousStage: 'Intake & Filing', newStage: 'Service', handoffDate: '2026-02-01' }),
      handoff({ id: 3, caseId: 10, previousStage: 'Service', newStage: 'Discovery & Evaluation', handoffDate: '2026-02-15' }),
      handoff({ id: 4, caseId: 20, previousStage: 'Service', newStage: 'Discovery & Evaluation', handoffDate: '2026-03-01' }),
    ]
    const dateOpenedByCaseId = new Map<number, string | null>([
      [10, '2026-01-01'],
      [20, '2026-01-10'],
    ])

    const segments = computeHandoffSegments(handoffs, dateOpenedByCaseId)

    expect(segments).toHaveLength(4)
    const case10Segments = segments.filter((s) => s.caseId === 10)
    const case20Segments = segments.filter((s) => s.caseId === 20)
    expect(case10Segments).toHaveLength(2)
    expect(case20Segments).toHaveLength(2)
    // Case 10's second segment must be anchored off case 10's own first handoff date (2026-01-15),
    // not case 20's, which sorts in between chronologically at the raw-array level.
    expect(case10Segments[1].days).toBe(31) // 2026-01-15 -> 2026-02-15
    expect(case20Segments[1].days).toBe(28) // 2026-02-01 -> 2026-03-01
  })

  it('skips (rather than clamps or flips) a transition whose handoffDate precedes its reference point - out-of-order/bad data', () => {
    const handoffs = [
      // handoffDate is BEFORE dateOpened - a negative gap.
      handoff({ id: 1, caseId: 30, previousStage: 'Intake & Filing', newStage: 'Service', handoffDate: '2025-12-01' }),
      // A valid second transition should still be computed normally, anchored off this bad row's
      // own handoffDate (the "immediately preceding reference point" is still updated even when
      // the segment itself was skipped).
      handoff({ id: 2, caseId: 30, previousStage: 'Service', newStage: 'Discovery & Evaluation', handoffDate: '2026-01-10' }),
    ]
    const dateOpenedByCaseId = new Map<number, string | null>([[30, '2026-01-01']])

    const segments = computeHandoffSegments(handoffs, dateOpenedByCaseId)

    // Only the second (valid) transition produces a segment; the first is silently dropped, not
    // reported with a negative or clamped-to-zero `days` value.
    expect(segments).toHaveLength(1)
    expect(segments[0].toStage).toBe('Discovery & Evaluation')
    expect(segments[0].days).toBe(40) // 2025-12-01 -> 2026-01-10
    expect(segments.every((s) => s.days >= 0)).toBe(true)
  })
})
