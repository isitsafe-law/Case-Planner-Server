import { useState } from 'react'
import { Btn } from '../ui/Btn'

export type ReportExportColumn = { key: string; label: string }
export type ReportExportRow = Record<string, unknown>

function displayValue(value: unknown): string {
  if (value == null) return ''
  if (typeof value === 'boolean') return value ? 'Yes' : 'No'
  return String(value)
}

function safeFilePart(value: string): string {
  return value.trim().replace(/[^a-z0-9]+/gi, '_').replace(/^_+|_+$/g, '') || 'Report'
}

export function reportExportFileName(title: string, extension: 'csv' | 'xlsx', date = new Date().toISOString().slice(0, 10)): string {
  return `CasePlanner_${safeFilePart(title)}_${date}.${extension}`
}

export function ReportExportActions({ reportId, scopeCaseIds = [], title, columns, rows, filters = {} }: { reportId: string; scopeCaseIds?: number[]; title: string; columns: ReportExportColumn[]; rows: ReportExportRow[]; filters?: Record<string, string> }) {
  const [busy, setBusy] = useState<'csv' | 'xlsx' | null>(null)
  const [error, setError] = useState<string | null>(null)
  const generatedAt = new Date().toISOString()

  const downloadCsv = () => {
    setError(null)
    setBusy('csv')
    try {
      const escape = (value: unknown) => `"${displayValue(value).replaceAll('"', '""')}"`
      const metadata = [[title], [`Generated: ${generatedAt}`], [`Filters: ${Object.entries(filters).map(([key, value]) => `${key}=${value}`).join('; ')}`], []]
      const csvRows = [...metadata, columns.map((column) => column.label), ...rows.map((row) => columns.map((column) => row[column.key]))]
      const csv = csvRows.map((row) => row.map(escape).join(',')).join('\r\n')
      const url = URL.createObjectURL(new Blob([`\uFEFF${csv}`], { type: 'text/csv;charset=utf-8' }))
      const link = document.createElement('a')
      link.href = url
      link.download = reportExportFileName(title, 'csv')
      link.click()
      URL.revokeObjectURL(url)
    } catch {
      setError('Unable to create the CSV export. Try again.')
    } finally {
      setBusy(null)
    }
  }

  const downloadExcel = async () => {
    setError(null)
    setBusy('xlsx')
    try {
      const response = await fetch('/api/reports/export.xlsx', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ reportId, scopeCaseIds, title, generatedAt, filters, fileName: reportExportFileName(title, 'xlsx'), columns, rows: rows.map((row) => Object.fromEntries(columns.map((column) => [column.key, displayValue(row[column.key])])) ) }),
      })
      if (!response.ok) throw new Error('export failed')
      const blob = await response.blob()
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = url
      link.download = reportExportFileName(title, 'xlsx')
      link.click()
      URL.revokeObjectURL(url)
    } catch {
      setError('Unable to create the Excel export. Try again.')
    } finally {
      setBusy(null)
    }
  }

  return <div className="ui-title-actions" aria-label={`${title} exports`}>
    <Btn disabled={busy != null} onClick={downloadCsv}>{busy === 'csv' ? 'Preparing…' : 'Export CSV'}</Btn>
    <Btn variant="primary" disabled={busy != null} onClick={() => void downloadExcel()}>{busy === 'xlsx' ? 'Preparing…' : 'Export Excel'}</Btn>
    {error && <span className="helper-text ui-cell-danger" role="alert">{error}</span>}
  </div>
}
