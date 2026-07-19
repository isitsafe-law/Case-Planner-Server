import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CommandPalette, type CommandGroup } from '../CommandPalette'

function groups(overrides?: { addCaseAction?: () => void; openCaseAction?: () => void }): CommandGroup[] {
  return [
    {
      label: 'Navigation',
      items: [
        { id: 'nav-dashboard', label: 'Go to Dashboard', action: () => {} },
        { id: 'nav-cases', label: 'Go to Cases', action: () => {} },
      ],
    },
    {
      label: 'Actions',
      items: [{ id: 'action-add-case', label: 'Add case', action: overrides?.addCaseAction ?? (() => {}) }],
    },
    {
      label: 'Cases',
      items: [{ id: 'case-1', label: 'Smith Tract — CV-24-001 · J1001', action: overrides?.openCaseAction ?? (() => {}) }],
    },
  ]
}

describe('CommandPalette', () => {
  it('renders nothing when closed', () => {
    render(<CommandPalette open={false} onClose={() => {}} groups={groups()} />)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('opens with all groups visible and focuses the search input', () => {
    render(<CommandPalette open onClose={() => {}} groups={groups()} />)
    expect(screen.getByRole('dialog', { name: 'Command palette' })).toBeInTheDocument()
    expect(screen.getByText('Go to Dashboard')).toBeInTheDocument()
    expect(screen.getByText('Add case')).toBeInTheDocument()
    expect(screen.getByText(/Smith Tract/)).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Command palette search' })).toHaveFocus()
  })

  it('filters items by a case-insensitive substring match and highlights the match', async () => {
    const user = userEvent.setup()
    render(<CommandPalette open onClose={() => {}} groups={groups()} />)

    await user.type(screen.getByRole('combobox'), 'dash')

    expect(screen.getByRole('option', { name: /Go to Dashboard/ })).toBeInTheDocument()
    expect(screen.queryByText('Go to Cases')).not.toBeInTheDocument()
    expect(screen.queryByText('Add case')).not.toBeInTheDocument()
    const mark = document.querySelector('mark')
    expect(mark).not.toBeNull()
    expect(mark?.textContent?.toLowerCase()).toBe('dash')
  })

  it('shows a no-results state when nothing matches', async () => {
    const user = userEvent.setup()
    render(<CommandPalette open onClose={() => {}} groups={groups()} />)
    await user.type(screen.getByRole('combobox'), 'zzz-no-match')
    expect(screen.getByText(/No matches/)).toBeInTheDocument()
  })

  it('notifies the caller of query changes via onQuery', async () => {
    const user = userEvent.setup()
    const onQuery = vi.fn()
    render(<CommandPalette open onClose={() => {}} groups={groups()} onQuery={onQuery} />)
    await user.type(screen.getByRole('combobox'), 'sm')
    expect(onQuery).toHaveBeenLastCalledWith('sm')
  })

  it('moves selection with ArrowDown and runs the active item on Enter, then closes', async () => {
    const user = userEvent.setup()
    const addCaseAction = vi.fn()
    const onClose = vi.fn()
    render(<CommandPalette open onClose={onClose} groups={groups({ addCaseAction })} />)

    const input = screen.getByRole('combobox')
    // First option (Go to Dashboard) is active by default; move down twice to land on "Add case".
    await user.type(input, '{ArrowDown}{ArrowDown}')
    await user.keyboard('{Enter}')

    expect(addCaseAction).toHaveBeenCalledTimes(1)
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('closes on Escape without running any action', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()
    const addCaseAction = vi.fn()
    render(<CommandPalette open onClose={onClose} groups={groups({ addCaseAction })} />)

    await user.type(screen.getByRole('combobox'), '{Escape}')

    expect(onClose).toHaveBeenCalledTimes(1)
    expect(addCaseAction).not.toHaveBeenCalled()
  })

  it('runs an item on click', async () => {
    const user = userEvent.setup()
    const openCaseAction = vi.fn()
    const onClose = vi.fn()
    render(<CommandPalette open onClose={onClose} groups={groups({ openCaseAction })} />)

    await user.click(screen.getByText(/Smith Tract/))

    expect(openCaseAction).toHaveBeenCalledTimes(1)
    expect(onClose).toHaveBeenCalledTimes(1)
  })
})
