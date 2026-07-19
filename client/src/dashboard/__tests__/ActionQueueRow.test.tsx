import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ActionQueueRow, type ActionQueueHandlers } from '../ActionQueueRow'
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

function renderRow(item: ActionQueueItem, handlers: ActionQueueHandlers, extra: { selected?: boolean; onToggleSelect?: (caseId: number) => void; county?: string | null } = {}) {
  return render(
    <table>
      <tbody>
        <ActionQueueRow
          item={item}
          handlers={handlers}
          selected={extra.selected ?? false}
          onToggleSelect={extra.onToggleSelect ?? vi.fn()}
          county={extra.county}
        />
      </tbody>
    </table>,
  )
}

describe('ActionQueueRow', () => {
  it('shows the required row hierarchy: case, case number, county, reason, posture, next action, review date, days inactive', () => {
    const item = makeItem()
    renderRow(item, makeHandlers(), { county: 'Craighead' })

    expect(screen.getByText('Johnson - Tract 14')).toBeInTheDocument()
    expect(screen.getByText('27CV-24-100 · Craighead')).toBeInTheDocument()
    expect(screen.getByText('Discovery strategy not selected')).toBeInTheDocument()
    expect(screen.getByText(/Answer filed 42 days ago/)).toBeInTheDocument()
    expect(screen.getByText(/Recommended: Decide whether to serve written discovery/)).toBeInTheDocument()
    expect(screen.getByText(/42 days since meaningful activity/)).toBeInTheDocument()
    expect(screen.getByText('July 15, 2026')).toBeInTheDocument()
  })

  it('only shows the related-warning count when there is more than one', () => {
    const { rerender } = renderRow(makeItem({ relatedWarningCount: 1 }), makeHandlers())
    expect(screen.queryByText(/related warnings/)).not.toBeInTheDocument()

    rerender(
      <table>
        <tbody>
          <ActionQueueRow item={makeItem({ relatedWarningCount: 3 })} handlers={makeHandlers()} selected={false} onToggleSelect={vi.fn()} />
        </tbody>
      </table>,
    )
    expect(screen.getByText(/3 related warnings/)).toBeInTheDocument()
  })

  it('shows a faint dash when there is no review date', () => {
    renderRow(makeItem({ reviewDate: null }), makeHandlers())
    expect(screen.getByText('—')).toBeInTheDocument()
  })

  it('opens the case when "Open case" is clicked', async () => {
    const handlers = makeHandlers()
    renderRow(makeItem(), handlers)
    await userEvent.click(screen.getByRole('button', { name: 'Open case' }))
    expect(handlers.onOpenCase).toHaveBeenCalledWith(1)
  })

  it('opens the case when the case name link is clicked', async () => {
    const handlers = makeHandlers()
    renderRow(makeItem(), handlers)
    await userEvent.click(screen.getByRole('button', { name: 'Johnson - Tract 14' }))
    expect(handlers.onOpenCase).toHaveBeenCalledWith(1)
  })

  it('expands the "Set next action" quick form and submits both fields', async () => {
    const handlers = makeHandlers()
    renderRow(makeItem(), handlers)

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
    renderRow(makeItem(), handlers)

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
    renderRow(makeItem(), handlers)

    await userEvent.click(screen.getByRole('button', { name: 'Plan next step' }))
    await userEvent.click(screen.getByRole('button', { name: 'Wait on someone or something' }))
    await userEvent.type(screen.getByLabelText('Waiting on'), "Owner's appraiser to respond")
    await userEvent.type(screen.getByLabelText('Expected response or event'), 'Appraisal report')
    fireEvent.change(screen.getByLabelText('Follow-up date'), { target: { value: '2026-08-01' } })
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(handlers.onMarkWaiting).toHaveBeenCalledWith(1, "Owner's appraiser to respond", 'Appraisal report', '2026-08-01')
  })

  it('submits the defer form with a reason and future review date', async () => {
    const handlers = makeHandlers()
    renderRow(makeItem(), handlers)

    await userEvent.click(screen.getByRole('button', { name: 'Plan next step' }))
    await userEvent.click(screen.getByRole('button', { name: 'Choose custom revisit date' }))
    await userEvent.type(screen.getByLabelText('Reason'), 'Waiting on settlement conference')
    fireEvent.change(screen.getByLabelText('Future review date'), { target: { value: '2026-09-01' } })
    await userEvent.click(screen.getByRole('button', { name: 'Defer' }))

    expect(handlers.onDefer).toHaveBeenCalledWith(1, 'Waiting on settlement conference', '2026-09-01')
  })

  it('toggles selection via the row checkbox', async () => {
    const onToggleSelect = vi.fn()
    renderRow(makeItem(), makeHandlers(), { onToggleSelect })
    await userEvent.click(screen.getByRole('checkbox', { name: 'Select Johnson - Tract 14' }))
    expect(onToggleSelect).toHaveBeenCalledWith(1)
  })

  it('shows "Set discovery strategy" only when the reason mentions an unselected strategy', () => {
    renderRow(makeItem({ reason: 'Discovery strategy not selected' }), makeHandlers())
    expect(screen.getByRole('button', { name: 'Set discovery strategy' })).toBeInTheDocument()
  })

  it('shows "Update deadline" and "Mark complete" only when there is a related deadline and the reason mentions a deadline', () => {
    renderRow(makeItem({ reason: 'Discovery strategy not selected' }), makeHandlers())
    expect(screen.queryByRole('button', { name: 'Update deadline' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Mark complete' })).not.toBeInTheDocument()
  })

  it('every quick-action trigger is keyboard-reachable (real buttons, not icon-only divs)', () => {
    renderRow(makeItem(), makeHandlers())
    for (const name of ['Open case', 'Plan next step', 'Add note']) {
      const button = screen.getByRole('button', { name })
      expect(button.tagName).toBe('BUTTON')
    }
  })
})
