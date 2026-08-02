import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DashboardDueDate, DashboardWorkActions } from '../DashboardWorkActions'

const item = { key: 'task-1', type: 'task' as const, caseId: 1, title: 'Review appraisal', dueDate: '2026-08-05', tab: 'work' }

describe('DashboardWorkActions', () => {
  it('keeps the primary action without duplicate case or date actions', () => {
    render(<DashboardWorkActions item={item} onComplete={vi.fn(async () => {})} />)
    expect(screen.getByRole('button', { name: 'Mark done' })).toBeInTheDocument()
    expect(screen.queryByText(/Open case/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/Change due date/i)).not.toBeInTheDocument()
  })
})

describe('DashboardDueDate', () => {
  it('opens the direct date editor and saves the selected date', async () => {
    const onSave = vi.fn(async () => {})
    render(<DashboardDueDate item={item} onSave={onSave} />)
    await userEvent.click(screen.getByRole('button', { name: 'Change due date for Review appraisal' }))
    await userEvent.clear(screen.getByLabelText('New due date for Review appraisal'))
    await userEvent.type(screen.getByLabelText('New due date for Review appraisal'), '2026-08-07')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(onSave).toHaveBeenCalledWith('2026-08-07', undefined)
  })

  it('requires a reason when changing a generated deadline', async () => {
    const onSave = vi.fn(async () => {})
    const deadline = { ...item, type: 'deadline' as const, title: 'Serve complaint', source: { isManual: false } }
    render(<DashboardDueDate item={deadline} onSave={onSave} />)
    await userEvent.click(screen.getByRole('button', { name: 'Change due date for Serve complaint' }))
    expect(screen.getByPlaceholderText('Reason for override')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
    await userEvent.type(screen.getByPlaceholderText('Reason for override'), 'Court order changed the schedule')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(onSave).toHaveBeenCalledWith('2026-08-05', 'Court order changed the schedule')
  })
})
