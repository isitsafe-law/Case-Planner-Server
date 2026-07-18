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

describe('FilingPipelinePanel', () => {
  it('defaults to the My Desk tab', () => {
    const pipeline: FilingPipelineView = { myDesk: [makeRow({ currentHolder: 'Attorney' })], waiting: [], allPipeline: [] }
    render(<FilingPipelinePanel pipeline={pipeline} onOpenCase={() => {}} onHandoff={() => {}} />)
    expect(screen.getByRole('tab', { name: /My Desk/ })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByText('Smith Tract 7')).toBeInTheDocument()
  })

  it('shows an explanatory empty state on My Desk when nothing is there', () => {
    const pipeline: FilingPipelineView = { myDesk: [], waiting: [makeRow()], allPipeline: [makeRow()] }
    render(<FilingPipelinePanel pipeline={pipeline} onOpenCase={() => {}} onHandoff={() => {}} />)
    expect(screen.getByText('Nothing on your desk right now')).toBeInTheDocument()
  })

  it('switches to the Waiting tab and shows its rows', async () => {
    const pipeline: FilingPipelineView = { myDesk: [], waiting: [makeRow()], allPipeline: [makeRow()] }
    render(<FilingPipelinePanel pipeline={pipeline} onOpenCase={() => {}} onHandoff={() => {}} />)

    await userEvent.click(screen.getByRole('tab', { name: /Waiting/ }))
    expect(screen.getByRole('tab', { name: /Waiting/ })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByText('Smith Tract 7')).toBeInTheDocument()
  })

  it('flags a missing current holder or stage visibly, not silently', async () => {
    const pipeline: FilingPipelineView = {
      myDesk: [],
      waiting: [makeRow({ currentHolder: null, pipelineStage: null })],
      allPipeline: [],
    }
    render(<FilingPipelinePanel pipeline={pipeline} onOpenCase={() => {}} onHandoff={() => {}} />)
    await userEvent.click(screen.getByRole('tab', { name: /Waiting/ }))
    const missingLabels = screen.getAllByText('Missing')
    expect(missingLabels.length).toBe(1)
  })

  it('invokes onHandoff for the right case when "Hand off" is clicked', async () => {
    const onHandoff = vi.fn()
    const pipeline: FilingPipelineView = { myDesk: [makeRow({ currentHolder: 'Attorney', caseId: 42 })], waiting: [], allPipeline: [] }
    render(<FilingPipelinePanel pipeline={pipeline} onOpenCase={() => {}} onHandoff={onHandoff} />)

    await userEvent.click(screen.getByRole('button', { name: 'Hand off' }))
    expect(onHandoff).toHaveBeenCalledWith(42)
  })
})
