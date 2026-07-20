import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { FilingPipelinePanel } from '../FilingPipelinePanel'
import type { FilingPipelineView, PreFilingTractRow } from '../types'

function makeRow(overrides: Partial<PreFilingTractRow> = {}): PreFilingTractRow {
  return {
    caseId: 1,
    tractOrOwnerName: 'Smith Tract 7',
    projectName: 'Highway 10 Widening',
    jobNumber: '020678',
    county: 'Pulaski',
    currentHolder: 'Legal Assistant',
    pipelineStage: 'With Legal Assistant',
    dateSentToCurrentHolder: '2026-07-01',
    priority: 'Normal',
    nextReviewDate: null,
    currentIssue: null,
    lastFollowUpDate: null,
    flagReason: null,
    ...overrides,
  }
}

function makePipeline(rows: PreFilingTractRow[]): FilingPipelineView {
  return { myDesk: rows.filter((r) => r.currentHolder === 'Attorney'), waiting: rows.filter((r) => r.currentHolder !== 'Attorney'), allPipeline: rows }
}

describe('FilingPipelinePanel', () => {
  it('renders a card per pre-filing case from the full pipeline', () => {
    const pipeline = makePipeline([makeRow()])
    render(<FilingPipelinePanel pipeline={pipeline} onOpenCase={() => {}} onHandoff={() => {}} />)
    expect(screen.getByText('Smith Tract 7')).toBeInTheDocument()
    expect(screen.getByText('Legal Assistant')).toBeInTheDocument()
  })

  it('shows an empty state when there are no pre-filing matters', () => {
    const pipeline = makePipeline([])
    render(<FilingPipelinePanel pipeline={pipeline} onOpenCase={() => {}} onHandoff={() => {}} />)
    expect(screen.getByText('No pre-filing matters right now')).toBeInTheDocument()
  })

  it('shows Unassigned when there is no current holder', () => {
    const pipeline = makePipeline([makeRow({ currentHolder: null })])
    render(<FilingPipelinePanel pipeline={pipeline} onOpenCase={() => {}} onHandoff={() => {}} />)
    expect(screen.getByText('Unassigned')).toBeInTheDocument()
  })

  it('computes days with holder from dateSentToCurrentHolder', () => {
    // Build the "10 days ago" date string using the same UTC-calendar-day arithmetic the
    // component uses, so the assertion doesn't depend on the machine's local timezone offset.
    const today = new Date()
    const todayUtc = Date.UTC(today.getFullYear(), today.getMonth(), today.getDate())
    const tenDaysAgo = new Date(todayUtc - 10 * 86_400_000)
    const dateStr = `${tenDaysAgo.getUTCFullYear()}-${String(tenDaysAgo.getUTCMonth() + 1).padStart(2, '0')}-${String(tenDaysAgo.getUTCDate()).padStart(2, '0')}`
    const pipeline = makePipeline([makeRow({ dateSentToCurrentHolder: dateStr })])
    render(<FilingPipelinePanel pipeline={pipeline} onOpenCase={() => {}} onHandoff={() => {}} />)
    expect(screen.getByText('10')).toBeInTheDocument()
  })

  it('invokes onOpenCase for the right case when "Open Case" is clicked', async () => {
    const onOpenCase = vi.fn()
    const pipeline = makePipeline([makeRow({ caseId: 42 })])
    render(<FilingPipelinePanel pipeline={pipeline} onOpenCase={onOpenCase} onHandoff={() => {}} />)

    await userEvent.click(screen.getByRole('button', { name: 'Open Case' }))
    expect(onOpenCase).toHaveBeenCalledWith(42)
  })

  it('invokes onHandoff for the right case when "Hand off" is clicked', async () => {
    const onHandoff = vi.fn()
    const pipeline = makePipeline([makeRow({ caseId: 42 })])
    render(<FilingPipelinePanel pipeline={pipeline} onOpenCase={() => {}} onHandoff={onHandoff} />)

    await userEvent.click(screen.getByRole('button', { name: 'Hand off' }))
    expect(onHandoff).toHaveBeenCalledWith(42)
  })

  it('shows a priority pill only when priority is not Normal', () => {
    const pipeline = makePipeline([makeRow({ priority: 'Rushed' })])
    render(<FilingPipelinePanel pipeline={pipeline} onOpenCase={() => {}} onHandoff={() => {}} />)
    expect(screen.getByText('Rushed')).toBeInTheDocument()
  })
})
