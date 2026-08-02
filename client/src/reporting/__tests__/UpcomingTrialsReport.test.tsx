import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { UpcomingTrialsReport } from '../UpcomingTrialsReport'
import type { CaseRecord, Hearing } from '../../App'

const makeCase = (id: number, attorney: string, division: string): CaseRecord => ({ id, caseName: `Case ${id}`, caseNumber: `C-${id}`, jobNumber: `J-${id}`, tract: `T-${id}`, county: 'Baxter', division, assignedAttorney: attorney, status: 'Active', caseStatus: 'Active Litigation', serviceRequired: false, servicePerfected: false })

describe('UpcomingTrialsReport', () => {
  it('renders Jury Trial events, including an event-only case, and filters by attorney', async () => {
    const cases = [makeCase(1, 'A. Attorney', 'Division 1'), makeCase(2, 'B. Attorney', 'Division 2')]
    const hearings: Hearing[] = [
      { id: 1, caseId: 1, title: 'Jury Trial', eventType: 'Jury Trial', hearingDate: '2099-08-15', createdAt: '', updatedAt: '' },
      { id: 2, caseId: 2, title: 'Jury Trial', eventType: 'Jury Trial', hearingDate: '2099-09-15', endDate: '2099-09-17', createdAt: '', updatedAt: '' },
    ]
    render(<UpcomingTrialsReport cases={cases} hearings={hearings} assignments={{}} attorneys={['A. Attorney', 'B. Attorney']} onOpenCase={vi.fn()} />)
    await userEvent.selectOptions(screen.getByLabelText('Horizon'), 'all')
    expect(screen.getByText('2 jury trials')).toBeInTheDocument()
    expect(screen.getByText('Case 1')).toBeInTheDocument()
    expect(screen.getByText('Case 2')).toBeInTheDocument()

    await userEvent.selectOptions(screen.getByLabelText('Attorney'), 'B. Attorney')
    expect(screen.queryByText('Case 1')).not.toBeInTheDocument()
    expect(screen.getByText('Case 2')).toBeInTheDocument()
  })
})
