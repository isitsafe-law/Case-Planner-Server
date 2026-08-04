import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { PreFilingMilestoneRecord } from '../../dashboard/types'
import {
  PreFilingMilestonesPanel,
  missingPrerequisiteLabel,
  laterMarkedMilestoneLabel,
} from '../PreFilingMilestonesPanel'

const apiMock = vi.fn()
vi.mock('../../App', () => ({
  api: (...args: unknown[]) => apiMock(...args),
}))

function makeRecord(overrides: Partial<PreFilingMilestoneRecord> = {}): PreFilingMilestoneRecord {
  return {
    id: 1,
    caseId: 1,
    milestone: 'PleadingsPackageSent',
    isMarked: false,
    ...overrides,
  }
}

beforeEach(() => {
  apiMock.mockReset()
})

describe('missingPrerequisiteLabel', () => {
  it('never blocks the first milestone in the order', () => {
    expect(missingPrerequisiteLabel([], 'PleadingsPackageSent')).toBeNull()
  })

  it('names the previous milestone when it is not yet marked', () => {
    expect(missingPrerequisiteLabel([], 'ChiefCounselSignaturesReceived')).toBe('Pleadings Package Sent')
  })

  it('returns null once the previous milestone is marked', () => {
    const milestones = [makeRecord({ milestone: 'PleadingsPackageSent', isMarked: true })]
    expect(missingPrerequisiteLabel(milestones, 'ChiefCounselSignaturesReceived')).toBeNull()
  })
})

describe('laterMarkedMilestoneLabel', () => {
  it('never blocks the last milestone in the order', () => {
    expect(laterMarkedMilestoneLabel([], 'DirectorSignatureReceived')).toBeNull()
  })

  it('returns null when nothing later is marked', () => {
    expect(laterMarkedMilestoneLabel([], 'PleadingsPackageSent')).toBeNull()
  })

  it('names the nearest later milestone that is still marked', () => {
    const milestones = [
      makeRecord({ milestone: 'ChiefCounselSignaturesReceived', isMarked: true }),
      makeRecord({ milestone: 'DeclarationOfTakingSentToDirector', isMarked: false }),
    ]
    expect(laterMarkedMilestoneLabel(milestones, 'PleadingsPackageSent')).toBe('Chief Counsel Signatures Received')
  })
})

describe('PreFilingMilestonesPanel', () => {
  const noMilestonesMarked: PreFilingMilestoneRecord[] = [
    makeRecord({ id: 1, milestone: 'PleadingsPackageSent' }),
    makeRecord({ id: 2, milestone: 'ChiefCounselSignaturesReceived' }),
    makeRecord({ id: 3, milestone: 'DeclarationOfTakingSentToDirector' }),
    makeRecord({ id: 4, milestone: 'DirectorSignatureReceived' }),
  ]

  it('loads and renders all 4 milestones in order, unmarked', async () => {
    apiMock.mockResolvedValueOnce(noMilestonesMarked)
    render(
      <PreFilingMilestonesPanel
        caseId={1}
        onOverrideReasonChange={() => {}}
        onMutated={async () => {}}
      />,
    )

    await waitFor(() => expect(screen.getAllByText('Not marked')).toHaveLength(4))
    expect(apiMock).toHaveBeenCalledWith('/api/cases/1/prefiling-milestones')
  })

  it('disables Mark for a milestone whose prerequisite is not yet marked', async () => {
    apiMock.mockResolvedValueOnce(noMilestonesMarked)
    render(
      <PreFilingMilestonesPanel
        caseId={1}
        onOverrideReasonChange={() => {}}
        onMutated={async () => {}}
      />,
    )

    await waitFor(() => expect(screen.getAllByText('Not marked')).toHaveLength(4))
    expect(screen.getByText('Pleadings Package Sent must be marked first.')).toBeInTheDocument()
    const markButtons = screen.getAllByRole('button', { name: 'Mark' })
    // First milestone's Mark button is enabled, the other 3 are disabled behind their prerequisite.
    expect(markButtons[0]).not.toBeDisabled()
    expect(markButtons[1]).toBeDisabled()
  })

  it('marks a milestone, refetches, and notifies the caller', async () => {
    const onMutated = vi.fn().mockResolvedValue(undefined)
    apiMock
      .mockResolvedValueOnce(noMilestonesMarked) // initial GET
      .mockResolvedValueOnce(null) // POST mark
      .mockResolvedValueOnce([ // refetch GET
        makeRecord({ id: 1, milestone: 'PleadingsPackageSent', isMarked: true, occurredDate: '2026-07-01' }),
        makeRecord({ id: 2, milestone: 'ChiefCounselSignaturesReceived' }),
        makeRecord({ id: 3, milestone: 'DeclarationOfTakingSentToDirector' }),
        makeRecord({ id: 4, milestone: 'DirectorSignatureReceived' }),
      ])

    render(
      <PreFilingMilestonesPanel
        caseId={7}
        onOverrideReasonChange={() => {}}
        onMutated={onMutated}
      />,
    )

    await waitFor(() => expect(screen.getAllByText('Not marked')).toHaveLength(4))
    await userEvent.click(screen.getAllByRole('button', { name: 'Mark' })[0])

    await waitFor(() => expect(onMutated).toHaveBeenCalled())
    expect(apiMock).toHaveBeenCalledWith(
      '/api/cases/7/prefiling-milestones/PleadingsPackageSent/mark',
      expect.objectContaining({ method: 'POST' }),
    )
    await waitFor(() => expect(screen.getAllByText('Marked')).toHaveLength(1))
  })

  it('requires a non-blank reason before Confirm Unmark is enabled', async () => {
    const marked: PreFilingMilestoneRecord[] = [
      makeRecord({ id: 1, milestone: 'PleadingsPackageSent', isMarked: true, occurredDate: '2026-07-01' }),
      makeRecord({ id: 2, milestone: 'ChiefCounselSignaturesReceived' }),
      makeRecord({ id: 3, milestone: 'DeclarationOfTakingSentToDirector' }),
      makeRecord({ id: 4, milestone: 'DirectorSignatureReceived' }),
    ]
    apiMock.mockResolvedValueOnce(marked)
    render(
      <PreFilingMilestonesPanel
        caseId={1}
        onOverrideReasonChange={() => {}}
        onMutated={async () => {}}
      />,
    )

    await waitFor(() => expect(screen.getByText('Marked')).toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: 'Unmark…' }))
    const confirmButton = screen.getByRole('button', { name: 'Confirm Unmark' })
    expect(confirmButton).toBeDisabled()

    await userEvent.type(screen.getByPlaceholderText('Why is this being reversed?'), 'Sent the wrong version.')
    expect(confirmButton).not.toBeDisabled()
  })

  it('sends onBehalfOfDisplay/onBehalfOfRole when marking on someone else\'s behalf', async () => {
    apiMock
      .mockResolvedValueOnce(noMilestonesMarked) // initial GET
      .mockResolvedValueOnce(null) // POST mark
      .mockResolvedValueOnce(noMilestonesMarked) // refetch GET

    render(
      <PreFilingMilestonesPanel
        caseId={7}
        onOverrideReasonChange={() => {}}
        onMutated={async () => {}}
      />,
    )

    await waitFor(() => expect(screen.getAllByText('Not marked')).toHaveLength(4))
    await userEvent.click(screen.getAllByRole('button', { name: 'Marking on someone else’s behalf?' })[0])
    await userEvent.type(screen.getByPlaceholderText('Leave blank if you are the approving party'), 'Jane Smith')
    await userEvent.type(screen.getByPlaceholderText('e.g. Chief Counsel'), 'Chief Counsel')
    await userEvent.click(screen.getAllByRole('button', { name: 'Mark' })[0])

    await waitFor(() => expect(apiMock).toHaveBeenCalledWith(
      '/api/cases/7/prefiling-milestones/PleadingsPackageSent/mark',
      expect.objectContaining({ method: 'POST' }),
    ))
    const markCall = apiMock.mock.calls.find(([url]) => url === '/api/cases/7/prefiling-milestones/PleadingsPackageSent/mark')
    const body = JSON.parse((markCall![1] as { body: string }).body)
    expect(body.onBehalfOfDisplay).toBe('Jane Smith')
    expect(body.onBehalfOfRole).toBe('Chief Counsel')
  })

  it('does not offer the on-behalf-of toggle for the Director signature milestone', async () => {
    apiMock.mockResolvedValueOnce(noMilestonesMarked)
    render(
      <PreFilingMilestonesPanel
        caseId={1}
        onOverrideReasonChange={() => {}}
        onMutated={async () => {}}
      />,
    )

    await waitFor(() => expect(screen.getAllByText('Not marked')).toHaveLength(4))
    expect(screen.getAllByRole('button', { name: 'Marking on someone else’s behalf?' })).toHaveLength(3)
  })

  it.skip('the removed Continue Without Marking control is no longer rendered', async () => {
    apiMock.mockResolvedValueOnce(noMilestonesMarked)
    render(
      <PreFilingMilestonesPanel
        caseId={1}
        onOverrideReasonChange={() => {}}
        onMutated={async () => {}}
      />,
    )

    await waitFor(() => expect(screen.getAllByText('Not marked')).toHaveLength(4))
    expect(screen.getByRole('button', { name: 'Continue Without Marking…' })).not.toBeDisabled()
  })

  it.skip('the removed Director signature override is no longer rendered', async () => {
    apiMock.mockResolvedValueOnce(noMilestonesMarked)
    render(
      <PreFilingMilestonesPanel
        caseId={1}
        onOverrideReasonChange={() => {}}
        onMutated={async () => {}}
        autoOpenOverride
      />,
    )

    await waitFor(() => expect(screen.getAllByText('Not marked')).toHaveLength(4))
    expect(screen.queryByRole('button', { name: 'Continue Without Marking…' })).not.toBeInTheDocument()
    expect(screen.getByPlaceholderText('Why is this case leaving Pipeline without the Director signature milestone?')).toBeInTheDocument()
  })
})
