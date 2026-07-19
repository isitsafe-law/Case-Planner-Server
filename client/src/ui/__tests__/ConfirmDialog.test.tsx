import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConfirmDialog, type ConfirmOptions } from '../ConfirmDialog'

function options(overrides?: Partial<ConfirmOptions>): ConfirmOptions {
  return {
    title: 'Delete deadline?',
    message: '"Answer discovery" will be permanently removed.',
    confirmLabel: 'Delete',
    danger: true,
    ...overrides,
  }
}

describe('ConfirmDialog', () => {
  it('renders the title, message, and default Cancel focus', () => {
    render(<ConfirmDialog options={options()} onConfirm={() => {}} onCancel={() => {}} />)
    expect(screen.getByRole('alertdialog', { name: /Delete deadline/ })).toBeInTheDocument()
    expect(screen.getByText('"Answer discovery" will be permanently removed.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cancel' })).toHaveFocus()
  })

  it('calls onConfirm when the confirm button is clicked', async () => {
    const user = userEvent.setup()
    const onConfirm = vi.fn()
    render(<ConfirmDialog options={options()} onConfirm={onConfirm} onCancel={() => {}} />)

    await user.click(screen.getByRole('button', { name: 'Delete' }))

    expect(onConfirm).toHaveBeenCalledTimes(1)
  })

  it('calls onCancel when the cancel button is clicked', async () => {
    const user = userEvent.setup()
    const onCancel = vi.fn()
    render(<ConfirmDialog options={options()} onConfirm={() => {}} onCancel={onCancel} />)

    await user.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(onCancel).toHaveBeenCalledTimes(1)
  })

  it('calls onCancel on Escape', async () => {
    const user = userEvent.setup()
    const onCancel = vi.fn()
    const onConfirm = vi.fn()
    render(<ConfirmDialog options={options()} onConfirm={onConfirm} onCancel={onCancel} />)

    await user.keyboard('{Escape}')

    expect(onCancel).toHaveBeenCalledTimes(1)
    expect(onConfirm).not.toHaveBeenCalled()
  })

  it('uses custom cancel/confirm labels when supplied', () => {
    render(<ConfirmDialog options={options({ confirmLabel: 'Clear date', cancelLabel: 'Keep date', danger: false })} onConfirm={() => {}} onCancel={() => {}} />)
    expect(screen.getByRole('button', { name: 'Clear date' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Keep date' })).toBeInTheDocument()
  })
})
