import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CloseCaseDialog } from '../CloseCaseDialog'

describe('CloseCaseDialog', () => {
  it('renders with Closed Date defaulted and focused', () => {
    render(<CloseCaseDialog initialClosedDate="2026-07-21" onSubmit={() => {}} onCancel={() => {}} />)
    expect(screen.getByRole('dialog', { name: /Close case/ })).toBeInTheDocument()
    expect(screen.getByLabelText('Closed Date')).toHaveValue('2026-07-21')
    expect(screen.getByLabelText('Closed Date')).toHaveFocus()
  })

  it('disables Mark Closed until Disposition Type and Final Judgment Amount are filled in', async () => {
    const user = userEvent.setup()
    render(<CloseCaseDialog initialClosedDate="2026-07-21" onSubmit={() => {}} onCancel={() => {}} />)

    expect(screen.getByRole('button', { name: 'Mark Closed' })).toBeDisabled()

    await user.selectOptions(screen.getByLabelText('Disposition Type'), 'Settlement')
    expect(screen.getByRole('button', { name: 'Mark Closed' })).toBeDisabled()

    await user.type(screen.getByLabelText('Final Judgment / Settlement Amount'), '50000')
    expect(screen.getByRole('button', { name: 'Mark Closed' })).toBeEnabled()
  })

  it('calls onSubmit with the collected details', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn()
    render(<CloseCaseDialog initialClosedDate="2026-07-21" onSubmit={onSubmit} onCancel={() => {}} />)

    await user.selectOptions(screen.getByLabelText('Disposition Type'), 'Jury Trial')
    await user.type(screen.getByLabelText('Final Judgment / Settlement Amount'), '125000.5')
    await user.click(screen.getByRole('button', { name: 'Mark Closed' }))

    expect(onSubmit).toHaveBeenCalledWith({ closedDate: '2026-07-21', dispositionType: 'Jury Trial', finalJudgmentAmount: 125000.5 })
  })

  it('calls onCancel when the cancel button is clicked, without submitting', async () => {
    const user = userEvent.setup()
    const onCancel = vi.fn()
    const onSubmit = vi.fn()
    render(<CloseCaseDialog initialClosedDate="2026-07-21" onSubmit={onSubmit} onCancel={onCancel} />)

    await user.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(onCancel).toHaveBeenCalledTimes(1)
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('calls onCancel on Escape', async () => {
    const user = userEvent.setup()
    const onCancel = vi.fn()
    render(<CloseCaseDialog initialClosedDate="2026-07-21" onSubmit={() => {}} onCancel={onCancel} />)

    await user.keyboard('{Escape}')

    expect(onCancel).toHaveBeenCalledTimes(1)
  })

  it('pre-fills Disposition Type and Final Judgment Amount when re-closing an already-closed case', () => {
    render(
      <CloseCaseDialog
        initialClosedDate="2026-07-21"
        initialDispositionType="Mediation"
        initialFinalJudgmentAmount={75000}
        onSubmit={() => {}}
        onCancel={() => {}}
      />,
    )
    expect(screen.getByLabelText('Disposition Type')).toHaveValue('Mediation')
    expect(screen.getByLabelText('Final Judgment / Settlement Amount')).toHaveValue(75000)
  })
})
