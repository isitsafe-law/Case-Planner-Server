import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ActionQueueFilters } from '../ActionQueueFilters'

describe('ActionQueueFilters', () => {
  it('calls onChange with the merged filter when a field changes', async () => {
    const onChange = vi.fn()
    render(<ActionQueueFilters filters={{}} counties={['Pulaski', 'Saline']} projects={['Highway 10']} onChange={onChange} />)

    await userEvent.selectOptions(screen.getByLabelText('Filter by county'), 'Saline')
    expect(onChange).toHaveBeenCalledWith({ county: 'Saline' })
  })

  it('only shows "Clear filters" once a filter is active', () => {
    const { rerender } = render(<ActionQueueFilters filters={{}} counties={[]} projects={[]} onChange={() => {}} />)
    expect(screen.queryByRole('button', { name: 'Clear filters' })).not.toBeInTheDocument()

    rerender(<ActionQueueFilters filters={{ county: 'Saline' }} counties={[]} projects={[]} onChange={() => {}} />)
    expect(screen.getByRole('button', { name: 'Clear filters' })).toBeInTheDocument()
  })

  it('clears all filters at once', async () => {
    const onChange = vi.fn()
    render(<ActionQueueFilters filters={{ county: 'Saline', priority: 'Rushed' }} counties={[]} projects={[]} onChange={onChange} />)
    await userEvent.click(screen.getByRole('button', { name: 'Clear filters' }))
    expect(onChange).toHaveBeenCalledWith({})
  })

  it('has an accessible search field', () => {
    render(<ActionQueueFilters filters={{}} counties={[]} projects={[]} onChange={() => {}} />)
    expect(screen.getByRole('search')).toBeInTheDocument()
    expect(screen.getByLabelText('Search matters')).toBeInTheDocument()
  })
})
