import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { AuthenticatedUserProfile } from '../../App'
import type { PreFilingMilestoneRecord } from '../../dashboard/types'
import {
  PreFilingMilestonesPanel,
  canOverrideFilingGate,
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

describe('canOverrideFilingGate', () => {
  it('allows the override when there is no authenticated user (local/no-auth mode)', () => {
    expect(canOverrideFilingGate(null)).toBe(true)
  })

  it('allows admins, managers, and either manager tier', () => {
    expect(canOverrideFilingGate({ isAdmin: true, isManager: false } as any)).toBe(true)
    expect(canOverrideFilingGate({ isAdmin: false, isManager: true } as any)).toBe(true)
    expect(canOverrideFilingGate({ isAdmin: false, isManager: false, managerTier: 'ChiefCounsel' } as any)).toBe(true)
    expect(canOverrideFilingGate({ isAdmin: false, isManager: false, managerTier: 'DeputyChiefCounsel' } as any)).toBe(true)
  })

  it('denies a plain Attorney (no admin/manager flag, no manager tier)', () => {
    expect(canOverrideFilingGate({ isAdmin: false, isManager: false, managerTier: null } as any)).toBe(false)
  })
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
        currentUser={null}
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
        currentUser={null}
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
        currentUser={null}
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
        currentUser={null}
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

  it('disables the Manager Override toggle for a plain Attorney', async () => {
    apiMock.mockResolvedValueOnce(noMilestonesMarked)
    const attorney = { isAdmin: false, isManager: false, managerTier: null } as unknown as AuthenticatedUserProfile
    render(
      <PreFilingMilestonesPanel
        caseId={1}
        currentUser={attorney}
        onOverrideReasonChange={() => {}}
        onMutated={async () => {}}
      />,
    )

    await waitFor(() => expect(screen.getAllByText('Not marked')).toHaveLength(4))
    expect(screen.getByRole('button', { name: 'Manager Override…' })).toBeDisabled()
  })
})
