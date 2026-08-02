import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ReportExportActions, reportExportFileName } from '../ReportExportActions'

describe('ReportExportActions', () => {
  it('uses a consistent sanitized filename', () => {
    expect(reportExportFileName('Upcoming Trials', 'csv', '2026-08-01')).toBe('CasePlanner_Upcoming_Trials_2026-08-01.csv')
  })

  it('sends the filtered rows to the shared Excel endpoint', async () => {
    const response = new Response(new Blob(['xlsx']))
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response))
    vi.stubGlobal('URL', { createObjectURL: vi.fn(() => 'blob:report'), revokeObjectURL: vi.fn() })
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)
    render(<ReportExportActions reportId="caseload" scopeCaseIds={[1]} title="Caseload" filters={{ attorney: 'A. Attorney' }} columns={[{ key: 'case', label: 'Case' }]} rows={[{ case: 'Case 1' }]} />)

    await userEvent.click(screen.getByRole('button', { name: 'Export Excel' }))
    expect(fetch).toHaveBeenCalledWith('/api/reports/export.xlsx', expect.objectContaining({ method: 'POST', body: expect.stringContaining('"reportId":"caseload"') }))
    expect(fetch).toHaveBeenCalledWith('/api/reports/export.xlsx', expect.objectContaining({ body: expect.stringContaining('A. Attorney') }))
    expect(click).toHaveBeenCalled()
    click.mockRestore()
  })
})
