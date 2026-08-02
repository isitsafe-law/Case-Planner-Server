import { describe, expect, it } from 'vitest'
import { buildManagerHardDateBars, buildManagerPipelineBars, buildManagerTrialBars } from '../ManagerDashboardVisuals'
import type { CaseRecord, DeadlineItem, Hearing } from '../../App'

const baseCase = (id: number, assignedAttorney = 'A. Attorney'): CaseRecord => ({ id, caseName: `Case ${id}`, caseNumber: `C-${id}`, jobNumber: `J-${id}`, tract: `T-${id}`, county: 'Baxter', status: 'Active', caseStatus: 'Active Litigation', serviceRequired: false, servicePerfected: false, assignedAttorney })
const hearing = (id: number, caseId: number, eventType: string, hearingDate: string): Hearing => ({ id, caseId, title: eventType, eventType, hearingDate, createdAt: '', updatedAt: '' })
const deadline = (id: number, caseId: number, dueDate: string): DeadlineItem => ({ id, caseId, title: 'Court deadline', dueDate, status: 'Open', sourceType: 'Manual', isManual: true, severity: 'Normal' })

describe('manager dashboard summaries', () => {
  it('counts hard-date records in non-overlapping windows and excludes ordinary events', () => {
    const cases = [baseCase(1), baseCase(2)]
    const hearings = [hearing(1, 1, 'Hearing', '2026-08-15'), hearing(2, 2, 'Meeting', '2026-08-15')]
    const deadlines = [deadline(1, 1, '2026-09-20')]
    const bars = buildManagerHardDateBars(cases, hearings, deadlines)
    expect(bars[0].count).toBe(1)
    expect(bars[1].count).toBe(1)
    expect(bars.reduce((sum, bar) => sum + bar.count, 0)).toBe(2)
  })

  it('uses Jury Trial events rather than legacy trial-date projections', () => {
    const first = { ...baseCase(1, 'Primary'), trialDate: '2026-09-01' }
    const second = { ...baseCase(2, 'Primary'), trialDate: '2026-09-02' }
    const bars = buildManagerTrialBars([first, second], [hearing(1, 1, 'Jury Trial', '2026-09-01')])
    expect(bars.find((bar) => bar.key === 'Primary')?.count).toBe(1)
  })

  it('groups pipeline records by the shared aging-stage projection', () => {
    const cases = [{ ...baseCase(1), caseStatus: 'Pipeline' }, { ...baseCase(2), caseStatus: 'Pipeline' }]
    const bars = buildManagerPipelineBars(cases, { buckets: [], cases: [{ caseId: 1, furthestMilestone: 'Pleadings Package Sent', daysSinceMarked: 10 }] })
    expect(bars.find((bar) => bar.key === 'Pleadings Package Sent')?.count).toBe(1)
    expect(bars.find((bar) => bar.key === 'None')?.count).toBe(1)
  })
})
