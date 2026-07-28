import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReviewNoteRecord } from '../../dashboard/types'
import { ReviewNotesLog, isReturnedForRevisionDecision } from '../ReviewNotesLog'

const apiMock = vi.fn()
vi.mock('../../App', () => ({
  api: (...args: unknown[]) => apiMock(...args),
}))

function makeNote(overrides: Partial<ReviewNoteRecord> = {}): ReviewNoteRecord {
  return {
    id: 1,
    caseId: 1,
    decision: 'Looks good',
    occurredDate: '2026-07-20',
    ...overrides,
  }
}

beforeEach(() => {
  apiMock.mockReset()
})

describe('isReturnedForRevisionDecision', () => {
  it('matches "Sent back for revision" case-insensitively', () => {
    expect(isReturnedForRevisionDecision('Sent back for revision')).toBe(true)
    expect(isReturnedForRevisionDecision('sent back for revision')).toBe(true)
    expect(isReturnedForRevisionDecision('SENT BACK FOR REVISION')).toBe(true)
    expect(isReturnedForRevisionDecision('  Sent back for revision  ')).toBe(true)
  })

  it('does not match other decisions', () => {
    expect(isReturnedForRevisionDecision('Looks good')).toBe(false)
    expect(isReturnedForRevisionDecision('Other')).toBe(false)
    expect(isReturnedForRevisionDecision('')).toBe(false)
  })
})

describe('ReviewNotesLog', () => {
  it('loads and renders existing notes', async () => {
    apiMock.mockResolvedValueOnce([
      makeNote({ id: 1, decision: 'Looks good', reviewerName: 'Helen Newberry', reviewerRole: 'Deputy Chief Counsel' }),
      makeNote({ id: 2, decision: 'Sent back for revision', comment: 'Needs a revised legal description.' }),
    ])
    render(<ReviewNotesLog caseId={1} />)

    await waitFor(() => expect(screen.getByText('Looks good')).toBeInTheDocument())
    expect(screen.getByText(/Helen Newberry/)).toBeInTheDocument()
    expect(screen.getByText('Sent back for revision')).toBeInTheDocument()
    expect(screen.getByText('Needs a revised legal description.')).toBeInTheDocument()
    expect(apiMock).toHaveBeenCalledWith('/api/cases/1/review-notes')
  })

  it('shows empty state when there are no notes', async () => {
    apiMock.mockResolvedValueOnce([])
    render(<ReviewNotesLog caseId={1} />)
    await waitFor(() => expect(screen.getByText('No review notes yet.')).toBeInTheDocument())
  })

  it('adds a note using a suggested decision, refetches, and notifies the caller', async () => {
    const onAdded = vi.fn().mockResolvedValue(undefined)
    apiMock
      .mockResolvedValueOnce([]) // initial GET
      .mockResolvedValueOnce(null) // POST
      .mockResolvedValueOnce([makeNote({ id: 5, decision: 'Sent back for revision' })]) // refetch GET

    render(<ReviewNotesLog caseId={7} onAdded={onAdded} />)
    await waitFor(() => expect(screen.getByText('No review notes yet.')).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: 'Add Review Note…' }))
    await userEvent.selectOptions(screen.getByLabelText('Decision'), 'Sent back for revision')
    await userEvent.click(screen.getByRole('button', { name: 'Add Note' }))

    await waitFor(() => expect(onAdded).toHaveBeenCalled())
    expect(apiMock).toHaveBeenCalledWith(
      '/api/cases/7/review-notes',
      expect.objectContaining({ method: 'POST' }),
    )
    const postCall = apiMock.mock.calls.find((call) => call[0] === '/api/cases/7/review-notes' && call[1]?.method === 'POST')
    expect(JSON.parse(postCall![1].body)).toMatchObject({ decision: 'Sent back for revision' })
  })

  it('requires a custom decision to be typed before Add Note is enabled when "Other" is selected', async () => {
    apiMock.mockResolvedValueOnce([])
    render(<ReviewNotesLog caseId={1} />)
    await waitFor(() => expect(screen.getByText('No review notes yet.')).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: 'Add Review Note…' }))
    await userEvent.selectOptions(screen.getByLabelText('Decision'), 'Other')
    const addButton = screen.getByRole('button', { name: 'Add Note' })
    expect(addButton).toBeDisabled()

    await userEvent.type(screen.getByLabelText('Decision (custom)'), 'Reviewed informally by email')
    expect(addButton).not.toBeDisabled()
  })

  it('cancel clears the form and collapses it without saving', async () => {
    apiMock.mockResolvedValueOnce([])
    render(<ReviewNotesLog caseId={1} />)
    await waitFor(() => expect(screen.getByText('No review notes yet.')).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: 'Add Review Note…' }))
    await userEvent.type(screen.getByLabelText('Reviewer name (optional)'), 'Someone')
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(screen.getByRole('button', { name: 'Add Review Note…' })).toBeInTheDocument()
    expect(apiMock).toHaveBeenCalledTimes(1)
  })
})
