import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { DashboardVisualSummaries } from '../DashboardVisualSummaries'

describe('DashboardVisualSummaries', () => {
  it('renders labeled urgency and hard-date values with keyboard-accessible buttons', () => {
    render(
      <DashboardVisualSummaries
        urgency={[{ key: 'overdue', label: 'Overdue', count: 3 }]}
        hardDates={[{ key: '0-30', label: 'Next 30 days', count: 2, detail: '1 event · 1 deadline' }]}
        onUrgency={vi.fn()}
        onHardDates={vi.fn()}
      />,
    )

    expect(screen.getByRole('button', { name: /Overdue: 3/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Next 30 days: 2/ })).toBeInTheDocument()
    expect(screen.getByText('1 event · 1 deadline')).toBeInTheDocument()
  })

  it('routes bar clicks to their matching callbacks', () => {
    const onUrgency = vi.fn()
    const onHardDates = vi.fn()
    render(
      <DashboardVisualSummaries
        urgency={[{ key: 'next7', label: 'Next 7 days', count: 4 }]}
        hardDates={[{ key: '31-60', label: '31-60 days', count: 1 }]}
        onUrgency={onUrgency}
        onHardDates={onHardDates}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: /Next 7 days: 4/ }))
    fireEvent.click(screen.getByRole('button', { name: /31-60 days: 1/ }))
    expect(onUrgency).toHaveBeenCalledWith({ key: 'next7', label: 'Next 7 days', count: 4 })
    expect(onHardDates).toHaveBeenCalledWith({ key: '31-60', label: '31-60 days', count: 1 })
  })
})
