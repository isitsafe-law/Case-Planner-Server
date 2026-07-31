import { afterEach, describe, expect, it, vi } from 'vitest'
import type { CaseRecord, DeadlineItem, Hearing } from '../../App'
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

function makeHearing(overrides: Partial<Hearing> = {}): Hearing {
  return { id: 1, caseId: 1, title: 'Hearing', eventType: 'Hearing', hearingDate: '2026-08-10', createdAt: '', updatedAt: '', ...overrides }
}

function makeDeadline(overrides: Partial<DeadlineItem> = {}): DeadlineItem {
  return { id: 1, caseId: 1, title: 'Deadline', dueDate: '2026-07-29', status: 'Open', sourceType: 'Manual', isManual: true, severity: 'normal', ...overrides }
}

afterEach(() => vi.useRealTimers())

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

  it('computes transparent workload signals without counting closed work or completed deadlines', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-07-30T12:00:00'))
    const rows = buildAttorneyRows(
      [
        makeCase({ id: 1, assignedAttorney: 'A', caseStatus: 'Pipeline' }),
        makeCase({ id: 2, assignedAttorney: 'A', status: 'Closed', caseStatus: 'Resolved / Closed' }),
      ],
      [makeHearing({ caseId: 1 })],
      [makeDeadline({ caseId: 1 }), makeDeadline({ id: 2, caseId: 1, status: 'Done' }), makeDeadline({ id: 3, caseId: 2 })],
    )
    const row = rows[0]
    expect(row.openTracts).toBe(1)
    expect(row.pipelineTracts).toBe(1)
    expect(row.eventsNext30).toBe(1)
    expect(row.overdueDeadlines).toBe(1)
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
