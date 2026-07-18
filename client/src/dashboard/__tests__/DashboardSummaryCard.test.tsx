import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DashboardSummaryCard } from '../DashboardSummaryCard'

describe('DashboardSummaryCard', () => {
  it('renders the label and value', () => {
    render(<DashboardSummaryCard label="Needs Judgment" value={9} active={false} onClick={() => {}} />)
    expect(screen.getByText('Needs Judgment')).toBeInTheDocument()
    expect(screen.getByText('9')).toBeInTheDocument()
  })

  it('is a real button with aria-pressed reflecting active state', () => {
    render(<DashboardSummaryCard label="Stalled" value={0} active onClick={() => {}} />)
    const button = screen.getByRole('button', { name: /stalled/i })
    expect(button).toHaveAttribute('aria-pressed', 'true')
  })

  it('calls onClick when clicked or activated via keyboard', async () => {
    const onClick = vi.fn()
    render(<DashboardSummaryCard label="On My Desk" value={2} active={false} onClick={onClick} />)
    const button = screen.getByRole('button', { name: /on my desk/i })

    await userEvent.click(button)
    expect(onClick).toHaveBeenCalledTimes(1)

    button.focus()
    await userEvent.keyboard('{Enter}')
    expect(onClick).toHaveBeenCalledTimes(2)
  })
})
