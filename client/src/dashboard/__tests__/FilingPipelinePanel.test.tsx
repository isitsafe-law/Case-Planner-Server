import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { FilingPipelinePanel, holderDistribution } from '../FilingPipelinePanel'
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
    // "Legal Assistant" now also appears once in the new holder distribution summary strip, so
    // there are 2 matches (summary + card) rather than a single one.
    expect(screen.getAllByText('Legal Assistant')).toHaveLength(2)
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

  it('renders a holder distribution summary strip above the card list', () => {
    const pipeline = makePipeline([
      makeRow({ caseId: 1, currentHolder: 'Legal Assistant' }),
      makeRow({ caseId: 2, currentHolder: 'Legal Assistant' }),
      makeRow({ caseId: 3, currentHolder: 'Attorney' }),
    ])
    render(<FilingPipelinePanel pipeline={pipeline} onOpenCase={() => {}} onHandoff={() => {}} />)
    const summary = document.querySelector('.pipeline-holder-summary')
    expect(summary).not.toBeNull()
    expect(summary?.textContent).toContain('Legal Assistant')
    expect(summary?.textContent).toContain('2')
  })
})

describe('holderDistribution', () => {
  it('buckets rows into the 4 linear holders plus Other, in a fixed order', () => {
    const rows = [
      makeRow({ currentHolder: 'Legal Assistant' }),
      makeRow({ currentHolder: 'Legal Assistant' }),
      makeRow({ currentHolder: 'Attorney' }),
      makeRow({ currentHolder: 'Deputy Chief Counsel' }),
      makeRow({ currentHolder: 'Chief Counsel' }),
      makeRow({ currentHolder: 'Other' }),
    ]
    expect(holderDistribution(rows)).toEqual([
      { holder: 'Legal Assistant', count: 2 },
      { holder: 'Attorney', count: 1 },
      { holder: 'Deputy Chief Counsel', count: 1 },
      { holder: 'Chief Counsel', count: 1 },
      { holder: 'Other', count: 1 },
    ])
  })

  it('counts a null/blank currentHolder and unrecognized legacy values (e.g. Filing Staff) under Other', () => {
    const rows = [
      makeRow({ currentHolder: null }),
      makeRow({ currentHolder: 'Filing Staff' }),
    ]
    const result = holderDistribution(rows)
    expect(result.find((entry) => entry.holder === 'Other')?.count).toBe(2)
    expect(result.filter((entry) => entry.holder !== 'Other').every((entry) => entry.count === 0)).toBe(true)
  })

  it('returns zero counts for every bucket when there are no rows', () => {
    expect(holderDistribution([])).toEqual([
      { holder: 'Legal Assistant', count: 0 },
      { holder: 'Attorney', count: 0 },
      { holder: 'Deputy Chief Counsel', count: 0 },
      { holder: 'Chief Counsel', count: 0 },
      { holder: 'Other', count: 0 },
    ])
  })
})
