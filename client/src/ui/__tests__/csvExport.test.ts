import { describe, expect, it } from 'vitest'
import { buildCsv } from '../csvExport'

describe('buildCsv', () => {
  it('returns an empty string for no rows', () => {
    expect(buildCsv([])).toBe('')
  })

  it('writes a header row followed by each data row, columns in first-row key order', () => {
    const csv = buildCsv([
      { Name: 'Alpha', Count: 3 },
      { Name: 'Beta', Count: 1 },
    ])
    expect(csv).toBe('Name,Count\r\nAlpha,3\r\nBeta,1')
  })

  it('quotes and escapes cells containing commas, quotes, or newlines', () => {
    const csv = buildCsv([
      { Note: 'Contains, a comma' },
      { Note: 'Has "quotes"' },
      { Note: 'Multi\nline' },
    ])
    expect(csv).toBe('Note\r\n"Contains, a comma"\r\n"Has ""quotes"""\r\n"Multi\nline"')
  })

  it('renders null/undefined cell values as empty strings', () => {
    const csv = buildCsv([{ A: null, B: undefined, C: 0 }])
    expect(csv).toBe('A,B,C\r\n,,0')
  })
})
