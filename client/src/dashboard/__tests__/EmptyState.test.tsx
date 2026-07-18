import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { EmptyState } from '../EmptyState'

describe('EmptyState', () => {
  it('explains what the section means, not just that it is empty', () => {
    render(<EmptyState title="No trial-track matters" description="This section appears once a case is marked Trial Track." />)
    expect(screen.getByText('No trial-track matters')).toBeInTheDocument()
    expect(screen.getByText(/appears once a case is marked/i)).toBeInTheDocument()
  })

  it('exposes a status role so it is announced to assistive tech', () => {
    render(<EmptyState title="Nothing here" description="Nothing to show yet." />)
    expect(screen.getByRole('status')).toBeInTheDocument()
  })
})
