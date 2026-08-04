import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { ManagerDashboard } from '../ManagerDashboard'
import type { CaseRecord } from '../../App'

const apiMock = vi.fn().mockResolvedValue({ generatedAt: '', scopeDefinition: '', issues: [] })
vi.mock('../../App', () => ({
  api: (...args: unknown[]) => apiMock(...args),
  Panel: ({ title, children }: { title: string; children: unknown }) => (
    <section>
      <h3>{title}</h3>
      {children as never}
    </section>
  ),
}))

function makeCase(overrides: Partial<CaseRecord> = {}): CaseRecord {
  return {
    id: 1,
    caseNumber: 'C-1',
    caseName: 'Case 1',
    jobNumber: 'J-1',
    tract: 'T-1',
    county: 'Pulaski',
    status: 'Active',
    caseStatus: 'Active Litigation',
    serviceRequired: false,
    servicePerfected: false,
    ...overrides,
  }
}

function renderDashboard(overrides: Partial<Parameters<typeof ManagerDashboard>[0]> = {}) {
  return render(
    <ManagerDashboard
      allCases={[]}
      hearings={[]}
      deadlines={[]}
      checklist={[]}
      serviceQueue={[]}
      openReminders={[]}
      preFilingMilestones={[]}
      preFilingMilestonesAging={null}
      reviewNotes={[]}
      pendingEventChangeIds={new Set()}
      onOpenCase={vi.fn()}
      {...overrides}
    />,
  )
}

beforeEach(() => {
  apiMock.mockClear()
})

// Legal Assistant Dashboard audit Phase 6: Division Overview's assistant risk/coverage panel.
describe('ManagerDashboard Legal Assistant Coverage panel', () => {
  it('shows the panel with zeroed counts when there is nothing to report', async () => {
    renderDashboard()
    await waitFor(() => expect(screen.getByText('Legal Assistant Coverage')).toBeInTheDocument())
    expect(screen.getByText('Waiting on Attorney').closest('.metric-tile')?.textContent).toContain('0')
    expect(screen.queryByText('Waiting on attorney')).not.toBeInTheDocument() // the exception list itself only renders when non-empty
  })

  it('counts open assistant work and its overdue subset from LegalAssistant-owned checklist items only', async () => {
    renderDashboard({
      checklist: [
        { id: 1, caseId: 1, task: 'Overdue assistant task', status: 'Not Started', ownerRole: 'LegalAssistant', dueDate: '2020-01-01', assignedStaffName: 'Ann' },
        { id: 2, caseId: 1, task: 'Open assistant task', status: 'Not Started', ownerRole: 'LegalAssistant', assignedStaffName: 'Ann' },
        { id: 3, caseId: 1, task: 'Attorney task', status: 'Not Started', ownerRole: 'Attorney' },
        { id: 4, caseId: 1, task: 'Done assistant task', status: 'Done', ownerRole: 'LegalAssistant' },
      ],
    })
    await waitFor(() => expect(screen.getByText('Legal Assistant Coverage')).toBeInTheDocument())
    expect(screen.getByText('2')).toBeInTheDocument() // Assistant Work count (Attorney-owned and Done excluded)
    expect(screen.getByText('1 overdue')).toBeInTheDocument()
  })

  it('flags assistant-owned work with no assignedStaffName as unassigned coverage gaps', async () => {
    renderDashboard({
      checklist: [
        { id: 1, caseId: 1, task: 'Needs an owner', status: 'Not Started', ownerRole: 'LegalAssistant' },
        { id: 2, caseId: 1, task: 'Already owned', status: 'Not Started', ownerRole: 'LegalAssistant', assignedStaffName: 'Ann' },
      ],
    })
    await waitFor(() => expect(screen.getByText('Legal Assistant Coverage')).toBeInTheDocument())
    expect(screen.getByText('Unassigned assistant work')).toBeInTheDocument()
    expect(screen.getByText('Needs an owner')).toBeInTheDocument()
    expect(screen.queryByText('Already owned')).not.toBeInTheDocument()
  })

  it('counts event-preparation risk only for overdue work linked to a proceeding', async () => {
    renderDashboard({
      checklist: [
        { id: 1, caseId: 1, task: 'Linked overdue', status: 'Not Started', relatedEventId: 5, dueDate: '2020-01-01' },
        { id: 2, caseId: 1, task: 'Linked not overdue', status: 'Not Started', relatedEventId: 5 },
      ],
      deadlines: [
        { id: 10, caseId: 1, title: 'Unlinked overdue deadline', status: 'Open', dueDate: '2020-01-01', sourceType: 'Manual', isManual: true, severity: 'normal' },
      ],
    })
    await waitFor(() => expect(screen.getByText('Legal Assistant Coverage')).toBeInTheDocument())
    expect(screen.getByText('1')).toBeInTheDocument() // only the linked+overdue item counts
  })

  it('lists open reminder threads under Waiting on Attorney and links to the case', async () => {
    const onOpenCase = vi.fn()
    renderDashboard({
      openReminders: [
        { id: 1, caseId: 42, eventType: 'Requested', requestedAction: 'Review discovery responses', targetAttorneyDisplay: 'Sample Attorney', followUpDate: '2026-08-20', status: 'Open', occurredAt: '', recordedAt: '' },
      ],
      onOpenCase,
    })
    await waitFor(() => expect(screen.getByText('Waiting on attorney')).toBeInTheDocument())
    expect(screen.getByText('Review discovery responses')).toBeInTheDocument()
    screen.getByText('Review discovery responses').closest('button')?.click()
    expect(onOpenCase).toHaveBeenCalledWith(42)
  })

  it('replaces the ad hoc filingDate-age service risk rule with real ServiceStatusEngine warning levels', async () => {
    // A case with no filingDate at all (the old rule would score this as 0 risk, since its
    // Date.now()-minus-filingDate math needs a filing date to run) still counts here because the
    // engine's own "missing" band - a data problem worth flagging - is included.
    renderDashboard({
      allCases: [makeCase({ id: 1, filingDate: null, servicePerfected: false, serviceRequired: true })],
      serviceQueue: [{ caseId: 1, warningLevel: 'missing' }],
    })
    await waitFor(() => expect(screen.getByText('Legal Assistant Coverage')).toBeInTheDocument())
    const serviceRiskTile = screen.getByText('Service Risk · 90+ days').closest('button')
    expect(serviceRiskTile?.textContent).toContain('1')
  })

  it('shows the pre-filing holder distribution strip with all 5 buckets, scoped to Pipeline cases only', async () => {
    renderDashboard({
      allCases: [
        makeCase({ id: 1, caseStatus: 'Pipeline', currentHolder: 'Attorney' }),
        makeCase({ id: 2, caseStatus: 'Active Litigation', currentHolder: 'Attorney' }),
      ],
    })
    await waitFor(() => expect(screen.getByText('Legal Assistant Coverage')).toBeInTheDocument())
    const summary = document.querySelector('.pipeline-holder-summary')
    expect(summary).not.toBeNull()
    const buckets = Array.from(summary!.querySelectorAll('.pipeline-holder-summary-item')).map((el) => el.textContent)
    expect(buckets).toEqual(['Legal Assistant0', 'Attorney1', 'Deputy Chief Counsel0', 'Chief Counsel0', 'Other0'])
  })

  it('does not render a per-assistant activity ranking table', async () => {
    renderDashboard({
      checklist: [{ id: 1, caseId: 1, task: 'x', status: 'Not Started', ownerRole: 'LegalAssistant', assignedStaffName: 'Ann' }],
    })
    await waitFor(() => expect(screen.getByText('Legal Assistant Coverage')).toBeInTheDocument())
    expect(screen.queryByText('Ann')).not.toBeInTheDocument()
  })
})
