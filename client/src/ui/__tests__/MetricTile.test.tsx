import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MetricTile } from '../MetricTile'

describe('MetricTile', () => {
  it('renders the label and value', () => {
    render(<MetricTile label="Immediate" value={9} />)
    expect(screen.getByText('Immediate')).toBeInTheDocument()
    expect(screen.getByText('9')).toBeInTheDocument()
  })

  it('renders as a plain non-interactive element when no onClick is given', () => {
    render(<MetricTile label="Awaiting triage" value={3} />)
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('is a real button with aria-pressed reflecting active state when onClick is given', () => {
    render(<MetricTile label="Stalled" value={0} active onClick={() => {}} />)
    const button = screen.getByRole('button', { name: /stalled/i })
    expect(button).toHaveAttribute('aria-pressed', 'true')
  })

  it('reflects an inactive state via aria-pressed="false"', () => {
    render(<MetricTile label="Momentum" value={9} active={false} onClick={() => {}} />)
    const button = screen.getByRole('button', { name: /momentum/i })
    expect(button).toHaveAttribute('aria-pressed', 'false')
  })

  it('calls onClick when clicked or activated via keyboard', async () => {
    const onClick = vi.fn()
    render(<MetricTile label="On My Desk" value={2} active={false} onClick={onClick} />)
    const button = screen.getByRole('button', { name: /on my desk/i })

    await userEvent.click(button)
    expect(onClick).toHaveBeenCalledTimes(1)

    button.focus()
    await userEvent.keyboard('{Enter}')
    expect(onClick).toHaveBeenCalledTimes(2)
  })
})
