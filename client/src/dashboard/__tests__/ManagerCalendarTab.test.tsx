import { describe, expect, it } from 'vitest'
import { CALENDAR_HORIZONS, countEventsInWindow } from '../ManagerCalendarTab'
import type { CaseRecord, Hearing } from '../../App'

const caseRecord = (id: number, trialDate?: string): CaseRecord => ({ id, caseName: `Case ${id}`, caseNumber: `C-${id}`, jobNumber: `J-${id}`, tract: `T-${id}`, county: 'Baxter', status: 'Open', caseStatus: 'Active Litigation', trialDate, serviceRequired: false, servicePerfected: false })

describe('ManagerCalendarTab horizons', () => {
  it('offers the complete long-range planning set and See All', () => {
    expect(CALENDAR_HORIZONS).toEqual([7, 30, 60, 90, 120, 180, 'all'])
  })

  it('counts event-backed trials, including an in-progress multi-day event, without legacy projections', () => {
    const cases = [caseRecord(1, '2026-08-10'), caseRecord(2, '2026-08-11')]
    const hearings: Hearing[] = [
      { id: 1, caseId: 1, title: 'Jury Trial', eventType: 'Jury Trial', hearingDate: '2026-08-01', endDate: '2026-08-03', createdAt: '', updatedAt: '' },
      { id: 2, caseId: 2, title: 'Jury Trial', eventType: 'Jury Trial', hearingDate: '2026-08-20', createdAt: '', updatedAt: '' },
    ]
    expect(countEventsInWindow(cases, hearings, 30)).toBe(2)
  })

  it('excludes canceled and completed events from the shared count', () => {
    const cases = [caseRecord(1), caseRecord(2)]
    const hearings: Hearing[] = [
      { id: 1, caseId: 1, title: 'Jury Trial', eventType: 'Jury Trial', hearingDate: '2026-08-10', status: 'Canceled', createdAt: '', updatedAt: '' },
      { id: 2, caseId: 2, title: 'Jury Trial', eventType: 'Jury Trial', hearingDate: '2026-08-11', status: 'Scheduled', createdAt: '', updatedAt: '' },
    ]
    expect(countEventsInWindow(cases, hearings, 30)).toBe(1)
  })
})
