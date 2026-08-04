import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { EventPreparationPage, type EventPreparationPageProps } from '../EventPreparationPage'
import type { ReminderRequestRecord } from '../types'

function makeReminder(overrides: Partial<ReminderRequestRecord> = {}): ReminderRequestRecord {
  return {
    id: 1,
    caseId: 1,
    eventType: 'Requested',
    requestedAction: 'Review discovery responses',
    requestedByDisplay: 'Sample Legal Assistant',
    followUpDate: '2026-08-10',
    status: 'Open',
    occurredAt: '2026-08-01T00:00:00Z',
    recordedAt: '2026-08-01T00:00:00Z',
    ...overrides,
  }
}

function makeProps(overrides: Partial<EventPreparationPageProps> = {}): EventPreparationPageProps {
  return {
    event: { id: 5, caseId: 1, eventType: 'Jury Trial', hearingDate: '2026-09-01' },
    caseRecord: { id: 1, caseName: 'Sample Case', assignedAttorney: 'Sample Attorney' },
    work: [],
    onBack: vi.fn(),
    onOpenCase: vi.fn(),
    onAddTask: vi.fn(),
    onAddDeadline: vi.fn(),
    onApplyTemplate: vi.fn(),
    onRecalculateDates: vi.fn(),
    onGetReminders: vi.fn().mockResolvedValue([]),
    onRequestReminder: vi.fn().mockResolvedValue(undefined),
    onResolveReminder: vi.fn().mockResolvedValue(undefined),
    onProposeDateChange: vi.fn(),
    onGetPendingDateChange: vi.fn().mockResolvedValue(null),
    onReviewDateChange: vi.fn(),
    ...overrides,
  }
}

describe('EventPreparationPage reminders', () => {
  it('loads reminder history on mount', async () => {
    const onGetReminders = vi.fn().mockResolvedValue([makeReminder()])
    render(<EventPreparationPage {...makeProps({ onGetReminders })} />)

    await waitFor(() => expect(screen.getByText(/Attorney reminders \(1\)/)).toBeInTheDocument())
    expect(screen.getByText('Review discovery responses')).toBeInTheDocument()
  })

  it('opens the reminder form and submits a new reminder tied to the event', async () => {
    const onRequestReminder = vi.fn().mockResolvedValue(undefined)
    render(<EventPreparationPage {...makeProps({ onRequestReminder })} />)

    await userEvent.click(screen.getByRole('button', { name: 'Remind Attorney' }))
    const textarea = screen.getByDisplayValue('Jury Trial preparation review')
    await userEvent.clear(textarea)
    await userEvent.type(textarea, 'Confirm exhibit list')
    fireEvent.change(screen.getByLabelText(/Follow up again on/), { target: { value: '2026-08-15' } })
    await userEvent.click(screen.getByRole('button', { name: 'Record Reminder' }))

    await waitFor(() => expect(onRequestReminder).toHaveBeenCalledWith(expect.objectContaining({
      relatedEventId: 5,
      requestedAction: 'Confirm exhibit list',
      targetAttorneyDisplay: 'Sample Attorney',
      followUpDate: '2026-08-15',
    })))
  })

  it('disables Record Reminder until a follow-up date is set', async () => {
    render(<EventPreparationPage {...makeProps()} />)
    await userEvent.click(screen.getByRole('button', { name: 'Remind Attorney' }))
    expect(screen.getByRole('button', { name: 'Record Reminder' })).toBeDisabled()
    fireEvent.change(screen.getByLabelText(/Follow up again on/), { target: { value: '2026-08-15' } })
    expect(screen.getByRole('button', { name: 'Record Reminder' })).not.toBeDisabled()
  })

  it('shows Add Follow-Up instead of Record Reminder, and a Resolve control, when a thread is already open', async () => {
    const onGetReminders = vi.fn().mockResolvedValue([makeReminder()])
    const onResolveReminder = vi.fn().mockResolvedValue(undefined)
    render(<EventPreparationPage {...makeProps({ onGetReminders, onResolveReminder })} />)

    await waitFor(() => expect(screen.getByText(/Attorney reminders \(1\)/)).toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: 'Remind Attorney' }))
    expect(screen.getByRole('button', { name: 'Add Follow-Up' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Resolve reminder' }))
    await waitFor(() => expect(onResolveReminder).toHaveBeenCalledWith({ relatedEventId: 5 }))
  })

  it('does not show a Resolve control when there is no open thread', async () => {
    const onGetReminders = vi.fn().mockResolvedValue([makeReminder({ status: 'Resolved', eventType: 'Resolved' })])
    render(<EventPreparationPage {...makeProps({ onGetReminders })} />)
    await waitFor(() => expect(screen.getByText(/Attorney reminders \(1\)/)).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: 'Resolve reminder' })).not.toBeInTheDocument()
  })
})
