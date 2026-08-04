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
        onOpenCase={() => {}}
        onOpenPreparation={() => {}}
      />,
    )

    expect(screen.getByText('Calendar the trial date')).toBeInTheDocument()
    expect(screen.getByText('1 open')).toBeInTheDocument()
  })
})
