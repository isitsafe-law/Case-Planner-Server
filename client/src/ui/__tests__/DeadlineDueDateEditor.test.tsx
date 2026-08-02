import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DeadlineDueDateEditor } from '../DeadlineDueDateEditor'
import type { DeadlineItem } from '../../App'

const deadline = (isManual: boolean): DeadlineItem => ({ id: 1, caseId: 1, title: 'Serve complaint', dueDate: '2026-08-10', status: 'Open', sourceType: isManual ? 'Manual' : 'Template', isManual, severity: 'normal' })

describe('DeadlineDueDateEditor', () => {
  it('saves ordinary deadlines directly', async () => {
    const onSave = vi.fn(async () => {})
    render(<DeadlineDueDateEditor item={deadline(true)} onSave={onSave} />)
    await userEvent.clear(screen.getByLabelText('Due date for Serve complaint'))
    await userEvent.type(screen.getByLabelText('Due date for Serve complaint'), '2026-08-12')
    expect(onSave).toHaveBeenCalledWith('2026-08-12')
  })

  it('allows a generated deadline override without a reason', async () => {
    const onSave = vi.fn(async () => {})
    render(<DeadlineDueDateEditor item={deadline(false)} onSave={onSave} />)
    await userEvent.click(screen.getByRole('button', { name: 'Save date' }))
    expect(onSave).toHaveBeenCalledWith('2026-08-10')
  })
})
