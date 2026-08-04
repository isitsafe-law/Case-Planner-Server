import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { PrefilingReviewEventRecord } from '../../dashboard/types'
import { RowIntakePanel } from '../RowIntakePanel'

const apiMock = vi.fn()
vi.mock('../../App', () => ({
  api: (...args: unknown[]) => apiMock(...args),
}))

function makeRound(overrides: Partial<PrefilingReviewEventRecord> = {}): PrefilingReviewEventRecord {
  return {
    id: 1,
    caseId: 1,
    eventType: 'TitleReview',
    occurredAt: '2026-07-01',
    recordedAt: '2026-07-01T00:00:00Z',
    outcome: 'In Title Review',
    reviewerDisplay: 'Jane Title Attorney',
    ...overrides,
  }
}

beforeEach(() => {
  apiMock.mockReset()
})

describe('RowIntakePanel', () => {
  it('shows the current ROW intake status and loads only TitleReview events', async () => {
    apiMock.mockResolvedValueOnce([
      makeRound({ id: 1 }),
      { id: 2, caseId: 1, eventType: 'Advance', occurredAt: '2026-06-01', recordedAt: '2026-06-01T00:00:00Z' },
    ])
    render(
      <RowIntakePanel caseId={1} rowIntakeStatus="In Title Review" onMutated={async () => {}} />,
    )

    expect(screen.getByText('In Title Review')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByText('Jane Title Attorney')).toBeInTheDocument())
    expect(apiMock).toHaveBeenCalledWith('/api/cases/1/prefiling-review/events')
    expect(screen.queryByText('No title-review rounds recorded yet.')).not.toBeInTheDocument()
  })

  it('shows a not-tracked placeholder when no status is set', async () => {
    apiMock.mockResolvedValueOnce([])
    render(<RowIntakePanel caseId={1} onMutated={async () => {}} />)
    expect(screen.getByText('Not tracked through ROW intake')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByText('No title-review rounds recorded yet.')).toBeInTheDocument())
  })

  it('requires a reviewer name before Record Round is enabled', async () => {
    apiMock.mockResolvedValueOnce([])
    render(<RowIntakePanel caseId={1} onMutated={async () => {}} />)

    await waitFor(() => expect(screen.getByText('No title-review rounds recorded yet.')).toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: 'Record Title-Review Round…' }))
    const recordButton = screen.getByRole('button', { name: 'Record Round' })
    expect(recordButton).toBeDisabled()

    await userEvent.type(screen.getByPlaceholderText('Who reviewed the title this round'), 'Jane Title Attorney')
    expect(recordButton).not.toBeDisabled()
  })

  it('records a round, refetches, and notifies the caller', async () => {
    const onMutated = vi.fn().mockResolvedValue(undefined)
    apiMock
      .mockResolvedValueOnce([]) // initial GET
      .mockResolvedValueOnce(null) // POST title-review
      .mockResolvedValueOnce([makeRound()]) // refetch GET

    render(<RowIntakePanel caseId={7} onMutated={onMutated} />)

    await waitFor(() => expect(screen.getByText('No title-review rounds recorded yet.')).toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: 'Record Title-Review Round…' }))
    await userEvent.type(screen.getByPlaceholderText('Who reviewed the title this round'), 'Jane Title Attorney')
    await userEvent.click(screen.getByRole('button', { name: 'Record Round' }))

    await waitFor(() => expect(onMutated).toHaveBeenCalled())
    expect(apiMock).toHaveBeenCalledWith(
      '/api/cases/7/prefiling-review/title-review',
      expect.objectContaining({ method: 'POST' }),
    )
    const postCall = apiMock.mock.calls.find(([url]) => url === '/api/cases/7/prefiling-review/title-review')
    const body = JSON.parse((postCall![1] as { body: string }).body)
    expect(body.reviewerDisplay).toBe('Jane Title Attorney')
    expect(body.outcome).toBe('In Title Review')
  })

  // Regression: the header pill must reflect the just-recorded round's outcome immediately, not
  // wait for the parent's rowIntakeStatus prop to catch up (onMutated triggers a parent refresh
  // that resolves independently and may lag - see App.tsx's loadInitial, which doesn't refetch the
  // open case's workspace).
  it('shows the newest round outcome even when the rowIntakeStatus prop is stale', async () => {
    apiMock.mockResolvedValueOnce([
      makeRound({ id: 1, outcome: 'Returned to ROW', reviewerDisplay: 'Jane Title Attorney' }),
    ])
    render(
      <RowIntakePanel caseId={1} rowIntakeStatus="In Title Review" onMutated={async () => {}} />,
    )

    await waitFor(() => expect(screen.getAllByText('Returned to ROW')).toHaveLength(2)) // header pill + round entry
  })
})
