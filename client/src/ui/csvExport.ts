// Generic, reusable CSV export for any table in this app (first consumer: the Manager/Administrator
// Dashboard's Calendar and Incoming Pipeline tables - Milestone 4). Intentionally separate from the
// Reports tab's existing ad-hoc exportReportCsv (App.tsx) which stays as-is; that one bakes in
// report-specific title/filter header rows this generic helper doesn't know about.

export type CsvCellValue = string | number | null | undefined

function escapeCsvCell(value: CsvCellValue): string {
  const text = value == null ? '' : String(value)
  // Only quote when required (RFC 4180): a bare comma, quote, or line break inside the cell.
  if (/[",\r\n]/.test(text)) {
    return `"${text.replaceAll('"', '""')}"`
  }
  return text
}

// Row order is preserved; column order follows the first row's key order, and every row is
// expected to share the same keys (the usual shape for a table export - one object per rendered
// row, keyed by column header).
export function buildCsv(rows: Record<string, CsvCellValue>[]): string {
  if (rows.length === 0) return ''
  const headers = Object.keys(rows[0])
  const lines = [headers, ...rows.map((row) => headers.map((header) => row[header]))]
  return lines.map((line) => line.map(escapeCsvCell).join(',')).join('\r\n')
}

// Triggers a browser download via a Blob + temporary <a> element. A UTF-8 BOM is prefixed so
// Excel opens the file with correct encoding instead of guessing (matches the existing Reports
// export's convention).
export function downloadCsv(filename: string, rows: Record<string, CsvCellValue>[]): void {
  const csv = buildCsv(rows)
  const blob = new Blob([`﻿${csv}`], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  link.click()
  URL.revokeObjectURL(url)
}
