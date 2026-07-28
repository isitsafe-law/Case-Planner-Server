import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { CaseRecord } from '../../App'
import type { PreFilingMilestoneRecord } from '../types'
import { BulkMilestoneGrid } from '../BulkMilestoneGrid'

const apiMock = vi.fn()
vi.mock('../../App', () => ({
  api: (...args: unknown[]) => apiMock(...args),
}))

function makeCase(overrides: Partial<CaseRecord> = {}): CaseRecord {
  return {
    id: 1,
    caseNumber: '',
    caseName: 'Fixture Case',
    jobNumber: 'JOB1',
    tract: '1',
    county: 'Pulaski',
    status: 'Pipeline',
    caseStatus: 'Pipeline',
    serviceRequired: true,
    servicePerfected: false,
    ...overrides,
  }
}

beforeEach(() => {
  apiMock.mockReset()
})

describe('BulkMilestoneGrid', () => {
  it('shows a prompt until a job number is loaded', () => {
    render(<BulkMilestoneGrid allCases={[]} preFilingMilestones={[]} onMutated={async () => {}} />)
    expect(screen.getByText('Enter a job number to load its tracts.')).toBeInTheDocument()
  })

  it('loads only Pipeline tracts matching the searched job number, case-insensitively', async () => {
    const allCases = [
      makeCase({ id: 1, jobNumber: 'JOB1', tract: '1', caseStatus: 'Pipeline' }),
      makeCase({ id: 2, jobNumber: 'JOB1', tract: '2', caseStatus: 'Pipeline' }),
      makeCase({ id: 3, jobNumber: 'JOB2', tract: '1', caseStatus: 'Pipeline' }),
      makeCase({ id: 4, jobNumber: 'JOB1', tract: '3', caseStatus: 'Active Litigation' }),
    ]
    render(<BulkMilestoneGrid allCases={allCases} preFilingMilestones={[]} onMutated={async () => {}} />)

    await userEvent.type(screen.getByLabelText('Job number'), 'job1')
    await userEvent.click(screen.getByRole('button', { name: 'Load Tracts' }))

    const rows = screen.getAllByRole('row')
    // header + 2 matching tracts (JOB1 Pipeline tracts 1 and 2) - JOB2 and the non-Pipeline JOB1 tract excluded.
    expect(rows).toHaveLength(3)
  })

  it('disables the checkbox for a milestone whose prerequisite is not yet marked', async () => {
    const allCases = [makeCase({ id: 1, jobNumber: 'JOB1', tract: '1' })]
    render(<BulkMilestoneGrid allCases={allCases} preFilingMilestones={[]} onMutated={async () => {}} />)
    await userEvent.type(screen.getByLabelText('Job number'), 'JOB1')
    await userEvent.click(screen.getByRole('button', { name: 'Load Tracts' }))

    const checkboxes = screen.getAllByRole('checkbox')
    expect(checkboxes[0]).not.toBeDisabled() // PleadingsPackageSent has no prerequisite
    expect(checkboxes[1]).toBeDisabled() // ChiefCounselSignaturesReceived requires PleadingsPackageSent
  })

  it('shows an already-marked milestone as a pill, not a checkbox', async () => {
    const allCases = [makeCase({ id: 1, jobNumber: 'JOB1', tract: '1' })]
    const milestones: PreFilingMilestoneRecord[] = [
      { id: 1, caseId: 1, milestone: 'PleadingsPackageSent', isMarked: true, occurredDate: '2026-07-01' },
    ]
    render(<BulkMilestoneGrid allCases={allCases} preFilingMilestones={milestones} onMutated={async () => {}} />)
    await userEvent.type(screen.getByLabelText('Job number'), 'JOB1')
    await userEvent.click(screen.getByRole('button', { name: 'Load Tracts' }))

    expect(screen.getByText('2026-07-01')).toBeInTheDocument()
    // Only 3 checkboxes remain (the 4th column, PleadingsPackageSent, is already marked).
    expect(screen.getAllByRole('checkbox')).toHaveLength(3)
  })

  it('selects tracts, marks them in one action, and notifies the caller', async () => {
    const onMutated = vi.fn().mockResolvedValue(undefined)
    const allCases = [
      makeCase({ id: 1, jobNumber: 'JOB1', tract: '1' }),
      makeCase({ id: 2, jobNumber: 'JOB1', tract: '2' }),
    ]
    apiMock.mockResolvedValueOnce({ batchId: 'b1', marked: [{}, {}], failures: [] })

    render(<BulkMilestoneGrid allCases={allCases} preFilingMilestones={[]} onMutated={onMutated} />)
    await userEvent.type(screen.getByLabelText('Job number'), 'JOB1')
    await userEvent.click(screen.getByRole('button', { name: 'Load Tracts' }))

    const checkboxes = screen.getAllByRole('checkbox')
    await userEvent.click(checkboxes[0]) // tract 1, PleadingsPackageSent
    await userEvent.click(checkboxes[4]) // tract 2, PleadingsPackageSent

    await userEvent.click(screen.getByRole('button', { name: 'Mark 2 tracts as Pleadings Package Sent…' }))
    await userEvent.click(screen.getByRole('button', { name: 'Confirm: Mark 2 Tracts' }))

    await waitFor(() => expect(onMutated).toHaveBeenCalled())
    expect(apiMock).toHaveBeenCalledWith(
      '/api/prefiling-milestones/bulk-mark',
      expect.objectContaining({ method: 'POST' }),
    )
    const body = JSON.parse(apiMock.mock.calls[0][1].body)
    expect(body.caseIds.sort()).toEqual([1, 2])
    expect(body.milestone).toBe('PleadingsPackageSent')

    await waitFor(() => expect(screen.getByText(/Marked 2 tracts/)).toBeInTheDocument())
  })

  it('shows an empty state when no Pipeline tracts match the job number', async () => {
    render(<BulkMilestoneGrid allCases={[]} preFilingMilestones={[]} onMutated={async () => {}} />)
    await userEvent.type(screen.getByLabelText('Job number'), 'NOPE')
    await userEvent.click(screen.getByRole('button', { name: 'Load Tracts' }))
    expect(screen.getByText('No Pipeline tracts found for job NOPE.')).toBeInTheDocument()
  })
})
