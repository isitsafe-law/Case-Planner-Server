import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { DashboardCompactSummaries } from '../DashboardCompactSummaries'

describe('DashboardCompactSummaries', () => {
  it('folds the next-trial card into the same panel as the event schedule', () => {
    const onJuryTrial = vi.fn()
    const onEvent = vi.fn()
    const onViewCalendar = vi.fn()
    render(<DashboardCompactSummaries planning={{ juryTrials: 2, events: 3, deadlines: 5, nextJuryTrial: { date: 'Sep. 14, 2026', caseName: 'Brown', caseId: 4, daysRemaining: 44 } }} schedule={[{ key: 'event-1', date: 'Sep. 20, 2026', kind: 'event', type: 'Hearing', title: 'Status hearing', caseId: 5, caseName: 'Jones', daysRemaining: 50 }]} onJuryTrial={onJuryTrial} onEvent={onEvent} onViewCalendar={onViewCalendar} />)
    expect(screen.queryByText(/Overdue/)).not.toBeInTheDocument()
    expect(screen.getByText('Upcoming Schedule')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Next jury trial/i }))
    fireEvent.click(screen.getByRole('button', { name: /Status hearing/ }))
    fireEvent.click(screen.getByRole('button', { name: 'View Calendar' }))
    expect(onJuryTrial).toHaveBeenCalledOnce()
    expect(onEvent).toHaveBeenCalledOnce()
    expect(onViewCalendar).toHaveBeenCalledOnce()
  })

  it('shows a placeholder when no jury trial is scheduled, without blocking the event list', () => {
    render(<DashboardCompactSummaries planning={{ juryTrials: 0, events: 1, deadlines: 0, nextJuryTrial: null }} schedule={[{ key: 'event-1', date: 'Sep. 20, 2026', kind: 'event', type: 'Hearing', title: 'Status hearing', caseId: 5, caseName: 'Jones', daysRemaining: 50 }]} onJuryTrial={() => {}} onEvent={() => {}} onViewCalendar={() => {}} />)
    expect(screen.getByRole('button', { name: 'No upcoming jury trial' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Status hearing/ })).toBeInTheDocument()
  })
})
