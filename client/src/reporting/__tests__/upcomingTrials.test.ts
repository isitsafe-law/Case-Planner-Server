import { describe, expect, it } from 'vitest'
import { upcomingJuryTrials } from '../upcomingTrials'

const cases = [
  { id: 1, caseName: 'Future', caseStatus: 'Active Litigation', status: 'Open' },
  { id: 2, caseName: 'Current multi-day', caseStatus: 'Trial Preparation', status: 'Open' },
  { id: 3, caseName: 'Past', caseStatus: 'Active Litigation', status: 'Open' },
  { id: 4, caseName: 'Legacy only', trialDate: '2026-09-20', caseStatus: 'Active Litigation', status: 'Open' },
  { id: 5, caseName: 'Closed', caseStatus: 'Resolved / Closed', status: 'Closed' },
]

describe('upcomingJuryTrials', () => {
  it('uses active Jury Trial events and includes an in-progress multi-day trial', () => {
    const result = upcomingJuryTrials(cases, [
      { id: 10, caseId: 1, eventType: 'Jury Trial', hearingDate: '2026-09-15', status: 'Scheduled' },
      { id: 11, caseId: 2, eventType: 'Jury Trial', hearingDate: '2026-08-01', endDate: '2026-08-03', status: 'Scheduled' },
      { id: 12, caseId: 3, eventType: 'Jury Trial', hearingDate: '2026-07-20', status: 'Scheduled' },
      { id: 13, caseId: 4, eventType: 'Hearing', hearingDate: '2026-09-20', status: 'Scheduled' },
      { id: 14, caseId: 5, eventType: 'Jury Trial', hearingDate: '2026-09-10', status: 'Scheduled' },
    ], '2026-08-02', 180)

    expect(result.map((row) => row.caseRecord.id)).toEqual([2, 1])
  })

  it('keeps a multi-day trial visible through its end date and excludes canceled events', () => {
    const result = upcomingJuryTrials(
      [{ id: 1, caseName: 'Current', caseStatus: 'Active Litigation', status: 'Open' }],
      [
        { id: 20, caseId: 1, eventType: 'Jury Trial', hearingDate: '2026-07-30', endDate: '2026-08-03', status: 'Scheduled' },
        { id: 21, caseId: 1, eventType: 'Jury Trial', hearingDate: '2026-08-10', status: 'Canceled' },
      ],
      '2026-08-02',
      30,
    )

    expect(result).toHaveLength(1)
    expect(result[0].event.id).toBe(20)
  })
})
