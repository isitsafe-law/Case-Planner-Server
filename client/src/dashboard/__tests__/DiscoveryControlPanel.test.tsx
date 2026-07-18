import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DiscoveryControlPanel } from '../DiscoveryControlPanel'
import type { DiscoveryControlSummary } from '../types'

function makeSummary(overrides: Partial<DiscoveryControlSummary> = {}): DiscoveryControlSummary {
  return {
    strategyNotSelected: 3,
    strategySelectedNotServed: 0,
    responsesOverdue: 0,
    responsesReceivedNotReviewed: 0,
    deficienciesUnresolved: 0,
    depositionDecisionPending: 0,
    cutoffApproaching: 0,
    complete: 0,
    noDiscoveryNeeded: 0,
    casesByCondition: {
      'Strategy not selected': [
        { caseId: 1, caseName: 'Johnson - Tract 14', caseNumber: '27CV-24-100', strategy: 'Strategy not selected', nextDecision: null, nextReviewDate: null },
      ],
    },
    ...overrides,
  }
}

describe('DiscoveryControlPanel', () => {
  it('shows an explanatory empty state when no filed cases exist yet', () => {
    render(<DiscoveryControlPanel summary={makeSummary({ strategyNotSelected: 0 })} onOpenCase={() => {}} />)
    expect(screen.getByText('No filed cases yet')).toBeInTheDocument()
  })

  it('renders all 9 discovery conditions with counts, disabling zero-count ones', () => {
    render(<DiscoveryControlPanel summary={makeSummary()} onOpenCase={() => {}} />)
    const chip = screen.getByRole('button', { name: /Strategy not selected/ })
    expect(chip).not.toBeDisabled()
    const zeroChip = screen.getByRole('button', { name: /Discovery complete/ })
    expect(zeroChip).toBeDisabled()
  })

  it('expands the case list for a condition on click, and opens a case from it', async () => {
    const onOpenCase = vi.fn()
    render(<DiscoveryControlPanel summary={makeSummary()} onOpenCase={onOpenCase} />)

    await userEvent.click(screen.getByRole('button', { name: /Strategy not selected/ }))
    const caseLink = screen.getByRole('button', { name: /Johnson - Tract 14/ })
    expect(caseLink).toBeInTheDocument()

    await userEvent.click(caseLink)
    expect(onOpenCase).toHaveBeenCalledWith(1)
  })
})
