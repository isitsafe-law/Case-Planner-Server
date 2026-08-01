import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { DashboardCompactSummaries } from '../DashboardCompactSummaries'

describe('DashboardCompactSummaries', () => {
  it('renders a compact next-trial card and event-only schedule', () => {
    const onJuryTrial = vi.fn()
    const onEvent = vi.fn()
    const onViewCalendar = vi.fn()
    render(<DashboardCompactSummaries planning={{ juryTrials: 2, events: 3, deadlines: 5, nextJuryTrial: { date: 'Sep. 14, 2026', caseName: 'Brown', caseId: 4, daysRemaining: 44 } }} schedule={[{ key: 'event-1', date: 'Sep. 20, 2026', kind: 'event', type: 'Hearing', title: 'Status hearing', caseId: 5, caseName: 'Jones', daysRemaining: 50 }]} onJuryTrial={onJuryTrial} onEvent={onEvent} onViewCalendar={onViewCalendar} />)
    expect(screen.queryByText(/Overdue/)).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Next jury trial/i }))
    fireEvent.click(screen.getByRole('button', { name: /Status hearing/ }))
    fireEvent.click(screen.getByRole('button', { name: 'View Calendar' }))
    expect(onJuryTrial).toHaveBeenCalledOnce()
    expect(onEvent).toHaveBeenCalledOnce()
    expect(onViewCalendar).toHaveBeenCalledOnce()
  })
})
