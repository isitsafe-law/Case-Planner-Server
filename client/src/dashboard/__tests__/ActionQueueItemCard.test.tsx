import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ActionQueueItemCard, type ActionQueueHandlers } from '../ActionQueueItemCard'
import type { ActionQueueItem } from '../types'

function makeItem(overrides: Partial<ActionQueueItem> = {}): ActionQueueItem {
  return {
    caseId: 1,
    caseName: 'Johnson - Tract 14',
    caseNumber: '27CV-24-100',
    jobNumber: '020678',
    currentPhase: 'Discovery & Evaluation',
    actionCategory: 'Decide',
    priorityLevel: 2,
    reason: 'Discovery strategy not selected',
    postureSummary: 'Answer filed 42 days ago. Owner alleges loss of access.',
    recommendedNextAction: 'Decide whether to serve written discovery.',
    reviewDate: '2026-07-15',
    daysSinceMeaningfulActivity: 42,
    relatedWarningCount: 1,
    currentHolder: null,
    matterType: 'FiledCase',
    ...overrides,
  }
}

function makeHandlers(overrides: Partial<ActionQueueHandlers> = {}): ActionQueueHandlers {
  return {
    onOpenCase: vi.fn(),
    onRecordDecision: vi.fn(),
    onSetNextAction: vi.fn().mockResolvedValue(undefined),
    onMarkWaiting: vi.fn().mockResolvedValue(undefined),
    onAddNote: vi.fn().mockResolvedValue(undefined),
    onDefer: vi.fn().mockResolvedValue(undefined),
    onAssignHolder: vi.fn().mockResolvedValue(undefined),
    ...overrides,
  }
}

describe('ActionQueueItemCard', () => {
  it('shows the required dashboard-row hierarchy: category, case, phase, reason, posture, next action, review date, days inactive, job number', () => {
    const item = makeItem()
    render(<ActionQueueItemCard item={item} handlers={makeHandlers()} />)

    expect(screen.getByText('DECIDE')).toBeInTheDocument()
    expect(screen.getByText(/Johnson - Tract 14/)).toBeInTheDocument()
    expect(screen.getByText('Discovery & Evaluation')).toBeInTheDocument()
    expect(screen.getByText('Discovery strategy not selected')).toBeInTheDocument()
    expect(screen.getByText(/Answer filed 42 days ago/)).toBeInTheDocument()
    expect(screen.getByText(/Decide whether to serve written discovery/)).toBeInTheDocument()
    expect(screen.getByText('Review by 2026-07-15')).toBeInTheDocument()
    expect(screen.getByText('42 days since meaningful activity')).toBeInTheDocument()
    expect(screen.getByText('Job 020678')).toBeInTheDocument()
  })

  it('only shows the related-warning count when there is more than one', () => {
    const { rerender } = render(<ActionQueueItemCard item={makeItem({ relatedWarningCount: 1 })} handlers={makeHandlers()} />)
    expect(screen.queryByText(/related warnings/)).not.toBeInTheDocument()

    rerender(<ActionQueueItemCard item={makeItem({ relatedWarningCount: 3 })} handlers={makeHandlers()} />)
    expect(screen.getByText('3 related warnings')).toBeInTheDocument()
  })

  it('opens the case when "Open case" is clicked', async () => {
    const handlers = makeHandlers()
    render(<ActionQueueItemCard item={makeItem()} handlers={handlers} />)
    await userEvent.click(screen.getByRole('button', { name: 'Open case' }))
    expect(handlers.onOpenCase).toHaveBeenCalledWith(1)
  })

  it('expands the "Set next action" quick form and submits both fields', async () => {
    const handlers = makeHandlers()
    render(<ActionQueueItemCard item={makeItem()} handlers={handlers} />)

    await userEvent.click(screen.getByRole('button', { name: 'Plan next step' }))
    await userEvent.click(screen.getByRole('button', { name: 'Set a next action' }))
    await userEvent.type(screen.getByLabelText('Next action'), 'Serve written discovery')
    // jsdom's <input type="date"> doesn't support keystroke-by-keystroke typing via
    // userEvent.type - fireEvent.change is the standard workaround for date inputs.
    fireEvent.change(screen.getByLabelText('Review date'), { target: { value: '2026-08-01' } })
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(handlers.onSetNextAction).toHaveBeenCalledWith(1, 'Serve written discovery', '2026-08-01')
  })

  it('closes the quick form after a successful save', async () => {
    const handlers = makeHandlers()
    render(<ActionQueueItemCard item={makeItem()} handlers={handlers} />)

    await userEvent.click(screen.getByRole('button', { name: 'Plan next step' }))
    await userEvent.click(screen.getByRole('button', { name: 'Set a next action' }))
    expect(screen.getByLabelText('Next action')).toBeInTheDocument()
    await userEvent.type(screen.getByLabelText('Next action'), 'x')
    fireEvent.change(screen.getByLabelText('Review date'), { target: { value: '2026-08-01' } })
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(screen.queryByLabelText('Next action')).not.toBeInTheDocument()
  })

  it('expands the "Mark as waiting" quick form and submits all three fields', async () => {
    const handlers = makeHandlers()
    render(<ActionQueueItemCard item={makeItem()} handlers={handlers} />)

    await userEvent.click(screen.getByRole('button', { name: 'Plan next step' }))
    await userEvent.click(screen.getByRole('button', { name: 'Wait on someone or something' }))
    await userEvent.type(screen.getByLabelText('Waiting on'), "Owner's appraiser to respond")
    await userEvent.type(screen.getByLabelText('Expected response or event'), 'Appraisal report')
    fireEvent.change(screen.getByLabelText('Follow-up date'), { target: { value: '2026-08-01' } })
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(handlers.onMarkWaiting).toHaveBeenCalledWith(1, "Owner's appraiser to respond", 'Appraisal report', '2026-08-01')
  })

  it('every quick-action trigger is keyboard-reachable (real buttons, not icon-only divs)', () => {
    render(<ActionQueueItemCard item={makeItem()} handlers={makeHandlers()} />)
    for (const name of ['Open case', 'Plan next step', 'Add note']) {
      const button = screen.getByRole('button', { name })
      expect(button.tagName).toBe('BUTTON')
    }
  })
})
