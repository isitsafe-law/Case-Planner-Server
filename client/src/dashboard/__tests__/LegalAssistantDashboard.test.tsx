import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { LegalAssistantDashboard } from '../LegalAssistantDashboard'

const cases = [
  { id: 1, caseName: 'State v. Doe', assignedAttorney: 'Jane Roe' },
]

describe('LegalAssistantDashboard owner_role filtering', () => {
  it('excludes Attorney-only tasks but keeps LegalAssistant/Either tasks and deadlines (no ownerRole field)', () => {
    const work = [
      { id: 1, caseId: 1, task: 'Attend hearing', status: 'Not Started', ownerRole: 'Attorney' },
      { id: 2, caseId: 1, task: 'Assemble Chief Counsel sign-off package', status: 'Not Started', ownerRole: 'LegalAssistant' },
      { id: 3, caseId: 1, task: 'Review discovery responses', status: 'Not Started', ownerRole: 'Either' },
      { id: 4, caseId: 1, title: 'Service deadline', status: 'Open' }, // deadline: no ownerRole field at all
    ]
    render(
      <LegalAssistantDashboard
        cases={cases}
        work={work}
        events={[]}
        serviceQueue={[]}
        publicationEntries={[]}
        onOpenCase={() => {}}
        onOpenPreparation={() => {}}
      />,
    )

    // 3 of the 4 items are visible (the Attorney-only task is excluded); the "On My Desk" KPI and
    // the panel's own count should both reflect that.
    expect(screen.getByText('3', { selector: '.metric-tile strong' })).toBeInTheDocument()
    expect(screen.getByText('3 open')).toBeInTheDocument()
    expect(screen.queryByText('Attend hearing')).not.toBeInTheDocument()
    expect(screen.getByText('Assemble Chief Counsel sign-off package')).toBeInTheDocument()
    expect(screen.getByText('Review discovery responses')).toBeInTheDocument()
    expect(screen.getByText('Service deadline')).toBeInTheDocument()
  })

  it('shows nothing as excluded when no work items are Attorney-only', () => {
    const work = [
      { id: 1, caseId: 1, task: 'Calendar the trial date', status: 'Not Started', ownerRole: 'LegalAssistant' },
    ]
    render(
      <LegalAssistantDashboard
        cases={cases}
        work={work}
        events={[]}
        serviceQueue={[]}
        publicationEntries={[]}
        onOpenCase={() => {}}
        onOpenPreparation={() => {}}
      />,
    )

    expect(screen.getByText('Calendar the trial date')).toBeInTheDocument()
    expect(screen.getByText('1 open')).toBeInTheDocument()
  })
})

// Legal Assistant Dashboard audit Phase 5: the Service and Publication section was rebuilt to use
// real ServiceStatusEngine output (serviceQueue) and publication proof exceptions instead of the
// old ad hoc caseStatus/servicePerfected boolean.
describe('LegalAssistantDashboard service and publication section', () => {
  it('excludes none/resolved/normal warning levels and sorts the rest by severity', () => {
    const serviceQueue = [
      { caseId: 1, caseName: 'Checkin Case', warningLevel: 'checkin', warningText: 'Service remains pending at the 60-day check-in point.' },
      { caseId: 1, caseName: 'Overdue Case', warningLevel: 'overdue', warningText: 'Service deadline has passed.' },
      { caseId: 1, caseName: 'Resolved Case', warningLevel: 'resolved', warningText: 'Service is perfected.' },
      { caseId: 1, caseName: 'Normal Case', warningLevel: 'normal', warningText: 'No action needed yet.' },
      { caseId: 1, caseName: 'None Case', warningLevel: 'none', warningText: '' },
    ]
    render(
      <LegalAssistantDashboard
        cases={cases}
        work={[]}
        events={[]}
        serviceQueue={serviceQueue}
        publicationEntries={[]}
        onOpenCase={() => {}}
        onOpenPreparation={() => {}}
      />,
    )

    expect(screen.getByText('Service deadline has passed.')).toBeInTheDocument()
    expect(screen.getByText('Service remains pending at the 60-day check-in point.')).toBeInTheDocument()
    expect(screen.queryByText('Service is perfected.')).not.toBeInTheDocument()
    expect(screen.queryByText('No action needed yet.')).not.toBeInTheDocument()

    // Overdue (rank 1) must render before checkin (rank 5) in document order.
    const rows = screen.getAllByRole('button').map((el) => el.textContent || '')
    const overdueIndex = rows.findIndex((text) => text.includes('Service deadline has passed.'))
    const checkinIndex = rows.findIndex((text) => text.includes('Service remains pending at the 60-day check-in point.'))
    expect(overdueIndex).toBeGreaterThanOrEqual(0)
    expect(overdueIndex).toBeLessThan(checkinIndex)
  })

  it('flags a "missing" warning level as a deadline-not-computed exception, not ordinary service pending', () => {
    render(
      <LegalAssistantDashboard
        cases={cases}
        work={[]}
        events={[]}
        serviceQueue={[{ caseId: 1, caseName: 'Missing Deadline Case', warningLevel: 'missing', warningText: 'No service deadline could be calculated.' }]}
        publicationEntries={[]}
        onOpenCase={() => {}}
        onOpenPreparation={() => {}}
      />,
    )
    expect(screen.getByText('Deadline not computed')).toBeInTheDocument()
  })

  it('lists publication entries with ProofFiled=false as outstanding, and excludes filed ones', () => {
    render(
      <LegalAssistantDashboard
        cases={cases}
        work={[]}
        events={[]}
        serviceQueue={[]}
        publicationEntries={[
          { id: 1, caseId: 1, publicationNumber: '1', newspaper: 'Arkansas Democrat-Gazette', proofFiled: false },
          { id: 2, caseId: 1, publicationNumber: '2', newspaper: 'Northwest Arkansas Democrat-Gazette', proofFiled: true },
        ]}
        onOpenCase={() => {}}
        onOpenPreparation={() => {}}
      />,
    )
    expect(screen.getByText('Publication proof outstanding')).toBeInTheDocument()
    expect(screen.getByText(/Arkansas Democrat-Gazette/)).toBeInTheDocument()
    expect(screen.queryByText(/Northwest Arkansas Democrat-Gazette/)).not.toBeInTheDocument()
  })

  it('scopes service and publication exceptions to supported cases only', () => {
    render(
      <LegalAssistantDashboard
        cases={cases}
        work={[]}
        events={[]}
        serviceQueue={[{ caseId: 999, caseName: 'Out Of Scope', warningLevel: 'overdue', warningText: 'Service deadline has passed.' }]}
        publicationEntries={[{ id: 1, caseId: 999, publicationNumber: '1', proofFiled: false }]}
        onOpenCase={() => {}}
        onOpenPreparation={() => {}}
      />,
    )
    expect(screen.getByText('No service follow-up is currently due.')).toBeInTheDocument()
    expect(screen.queryByText('Publication proof outstanding')).not.toBeInTheDocument()
  })
})
