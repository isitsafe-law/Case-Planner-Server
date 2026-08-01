import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { DashboardCompactSummaries } from '../DashboardCompactSummaries'

describe('DashboardCompactSummaries', () => {
  it('renders compact counts and routes each category independently', () => {
    const onUrgency = vi.fn()
    const onJuryTrials = vi.fn()
    const onEvents = vi.fn()
    const onDeadlines = vi.fn()
    render(<DashboardCompactSummaries urgency={[{ key: 'overdue', label: 'Overdue', count: 4 }]} planning={{ juryTrials: 2, events: 3, deadlines: 5, nextJuryTrial: { date: 'Sep. 14, 2026', caseName: 'Brown' } }} onUrgency={onUrgency} onJuryTrials={onJuryTrials} onEvents={onEvents} onDeadlines={onDeadlines} />)
    expect(screen.getByRole('button', { name: 'Overdue: 4' })).toBeVisible()
    fireEvent.click(screen.getByRole('button', { name: /Next jury trial/i }))
    fireEvent.click(screen.getByRole('button', { name: 'Events · 30 days: 3' }))
    fireEvent.click(screen.getByRole('button', { name: 'Deadlines · 30 days: 5' }))
    expect(onJuryTrials).toHaveBeenCalledOnce()
    expect(onEvents).toHaveBeenCalledOnce()
    expect(onDeadlines).toHaveBeenCalledOnce()
  })
})
