import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { HOLDER_STEPS, HolderPipelineStepper, holderStepIndex, holderStepState } from '../HolderPipelineStepper'

describe('holderStepIndex', () => {
  it('finds the index of each of the 4 linear holders', () => {
    expect(holderStepIndex('Legal Assistant')).toBe(0)
    expect(holderStepIndex('Attorney')).toBe(1)
    expect(holderStepIndex('Deputy Chief Counsel')).toBe(2)
    expect(holderStepIndex('Chief Counsel')).toBe(3)
  })

  it('returns -1 for Other, an unrecognized value, or nothing set', () => {
    expect(holderStepIndex('Other')).toBe(-1)
    expect(holderStepIndex('Filing Staff')).toBe(-1)
    expect(holderStepIndex(null)).toBe(-1)
    expect(holderStepIndex(undefined)).toBe(-1)
    expect(holderStepIndex('')).toBe(-1)
  })
})

describe('holderStepState', () => {
  it('marks steps before the current index as completed', () => {
    expect(holderStepState(0, 2)).toBe('completed')
    expect(holderStepState(1, 2)).toBe('completed')
  })

  it('marks the current index as current', () => {
    expect(holderStepState(2, 2)).toBe('current')
  })

  it('marks steps after the current index as upcoming', () => {
    expect(holderStepState(3, 2)).toBe('upcoming')
  })

  it('treats every step as upcoming when the current holder is off the line (Other/unset)', () => {
    expect(holderStepState(0, -1)).toBe('upcoming')
    expect(holderStepState(3, -1)).toBe('upcoming')
  })
})

describe('HolderPipelineStepper', () => {
  it('renders all 4 linear steps plus Other', () => {
    render(<HolderPipelineStepper currentHolder="Attorney" onSelect={() => {}} />)
    HOLDER_STEPS.forEach((step) => expect(screen.getByRole('button', { name: step })).toBeInTheDocument())
    expect(screen.getByRole('button', { name: 'Other' })).toBeInTheDocument()
  })

  it('marks the current step with aria-current="step" and earlier steps as completed', () => {
    render(<HolderPipelineStepper currentHolder="Deputy Chief Counsel" onSelect={() => {}} />)
    const current = screen.getByRole('button', { name: 'Deputy Chief Counsel' })
    expect(current).toHaveAttribute('aria-current', 'step')
    expect(current.className).toContain('holder-step-current')
    expect(screen.getByRole('button', { name: 'Legal Assistant' }).className).toContain('holder-step-completed')
    expect(screen.getByRole('button', { name: 'Attorney' }).className).toContain('holder-step-completed')
    expect(screen.getByRole('button', { name: 'Chief Counsel' }).className).toContain('holder-step-upcoming')
  })

  it('marks Other as current when it is the active holder, and steps are all upcoming', () => {
    render(<HolderPipelineStepper currentHolder="Other" onSelect={() => {}} />)
    const other = screen.getByRole('button', { name: 'Other' })
    expect(other).toHaveAttribute('aria-current', 'step')
    expect(other.className).toContain('holder-step-current')
    HOLDER_STEPS.forEach((step) => expect(screen.getByRole('button', { name: step }).className).toContain('holder-step-upcoming'))
  })

  it('calls onSelect with the clicked holder, including a non-adjacent jump', async () => {
    const onSelect = vi.fn()
    render(<HolderPipelineStepper currentHolder="Legal Assistant" onSelect={onSelect} />)
    await userEvent.click(screen.getByRole('button', { name: 'Chief Counsel' }))
    expect(onSelect).toHaveBeenCalledWith('Chief Counsel')
  })

  it('calls onSelect with Other when the Other badge is clicked', async () => {
    const onSelect = vi.fn()
    render(<HolderPipelineStepper currentHolder="Attorney" onSelect={onSelect} />)
    await userEvent.click(screen.getByRole('button', { name: 'Other' }))
    expect(onSelect).toHaveBeenCalledWith('Other')
  })

  it('still calls onSelect when clicking the already-active step (caller decides it is a no-op)', async () => {
    const onSelect = vi.fn()
    render(<HolderPipelineStepper currentHolder="Attorney" onSelect={onSelect} />)
    await userEvent.click(screen.getByRole('button', { name: 'Attorney' }))
    expect(onSelect).toHaveBeenCalledWith('Attorney')
  })

  it('is keyboard-activatable via Enter on a focused step', async () => {
    const onSelect = vi.fn()
    render(<HolderPipelineStepper currentHolder="Legal Assistant" onSelect={onSelect} />)
    const button = screen.getByRole('button', { name: 'Attorney' })
    button.focus()
    await userEvent.keyboard('{Enter}')
    expect(onSelect).toHaveBeenCalledWith('Attorney')
  })
})
