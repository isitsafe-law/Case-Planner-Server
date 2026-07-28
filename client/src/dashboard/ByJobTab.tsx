import { Fragment, useMemo, useState } from 'react'
import type { CaseRecord, Hearing } from '../App'
import { Btn } from '../ui/Btn'
import { downloadCsv } from '../ui/csvExport'
import { formatDate } from '../ui/format'
import { EmptyState } from './EmptyState'
import { formatCurrencyOrDash, nextHardDate, statusDistribution, type NextHardDate, type StatusCount } from './dashboardAggregation'
import { StatusDistributionBar, StatusDistributionLegend } from './StatusDistributionBar'

// Label for cases with no job number recorded. Not an error state - Pipeline tracts legitimately
// lack a court-assigned job number yet, same as they legitimately lack other filed-case fields
// (see IncomingPipelinePanel.tsx's own precedent for treating pre-filing blanks as normal).
const NO_JOB_NUMBER = 'No Job Number'

export type JobRow = {
  jobNumber: string
  cases: CaseRecord[]
  tractCount: number
  distribution: StatusCount[]
  attorneys: string[]
  nextHard: NextHardDate | null
  // A job with every tract's depositAmount null legitimately sums to $0 - that's the correct/
  // expected value for a job with no Estimate of Just Compensation deposit recorded anywhere yet,
  // not an error state to special-case.
  totalDeposit: number
}

// Groups allCases by jobNumber (blank -> the NO_JOB_NUMBER bucket) and computes each column's value
// for every group. Exported for unit testing independent of the rendered table.
export function buildJobRows(allCases: CaseRecord[], hearings: Hearing[]): JobRow[] {
  const groups = new Map<string, CaseRecord[]>()
  for (const record of allCases) {
    const jobNumber = record.jobNumber || NO_JOB_NUMBER
    if (!groups.has(jobNumber)) groups.set(jobNumber, [])
    groups.get(jobNumber)!.push(record)
  }

  return Array.from(groups.entries()).map(([jobNumber, cases]) => {
    const attorneySet = new Set<string>()
    let totalDeposit = 0
    for (const record of cases) {
      attorneySet.add(record.assignedAttorney || 'Unassigned')
      totalDeposit += record.depositAmount ?? 0
    }
    return {
      jobNumber,
      cases,
      tractCount: cases.length,
      distribution: statusDistribution(cases),
      attorneys: Array.from(attorneySet).sort(),
      nextHard: nextHardDate(cases, hearings),
      totalDeposit,
    }
  })
}

type JobSortColumn = 'jobNumber' | 'tracts' | 'distribution' | 'attorneys' | 'nextHardDate' | 'totalDeposit'

// Sortable on every column, matching ByAttorneyTab's approach. "Distribution" sorts by total tract
// count (same judgment call as ByAttorneyTab's stacked-bar column - there's no other single sort
// key for a multi-segment bar). "Attorneys" sorts by the joined, already-alphabetized name list.
// Undated "next hard date" rows always sort last regardless of direction.
export function sortJobRows(rows: JobRow[], column: JobSortColumn, direction: 'asc' | 'desc'): JobRow[] {
  const dir = direction === 'asc' ? 1 : -1
  return [...rows].sort((a, b) => {
    switch (column) {
      case 'jobNumber':
        return dir * a.jobNumber.localeCompare(b.jobNumber, undefined, { numeric: true })
      case 'tracts':
      case 'distribution':
        return dir * (a.tractCount - b.tractCount)
      case 'attorneys':
        return dir * a.attorneys.join(', ').localeCompare(b.attorneys.join(', '))
      case 'totalDeposit':
        return dir * (a.totalDeposit - b.totalDeposit)
      case 'nextHardDate': {
        if (!a.nextHard && !b.nextHard) return 0
        if (!a.nextHard) return 1
        if (!b.nextHard) return -1
        return dir * a.nextHard.date.localeCompare(b.nextHard.date)
      }
      default:
        return 0
    }
  })
}

const COLUMNS: { key: JobSortColumn; label: string }[] = [
  { key: 'jobNumber', label: 'Job Number' },
  { key: 'tracts', label: 'Tract Count' },
  { key: 'distribution', label: 'Status Distribution' },
  { key: 'attorneys', label: 'Attorneys Assigned' },
  { key: 'nextHardDate', label: 'Earliest Upcoming Event' },
  { key: 'totalDeposit', label: 'Total Est. of Just Compensation Deposited' },
]

export function ByJobTab({
  allCases,
  hearings,
  onOpenCase,
}: {
  allCases: CaseRecord[]
  hearings: Hearing[]
  onOpenCase: (caseId: number) => void
}) {
  const [sortColumn, setSortColumn] = useState<JobSortColumn>('jobNumber')
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('asc')
  const [expanded, setExpanded] = useState<Set<string>>(new Set())

  const rows = useMemo(() => buildJobRows(allCases, hearings), [allCases, hearings])
  const sortedRows = useMemo(() => sortJobRows(rows, sortColumn, sortDirection), [rows, sortColumn, sortDirection])

  function toggleSort(column: JobSortColumn) {
    if (sortColumn === column) {
      setSortDirection((current) => (current === 'asc' ? 'desc' : 'asc'))
    } else {
      setSortColumn(column)
      setSortDirection('asc')
    }
  }

  function toggleExpanded(jobNumber: string) {
    setExpanded((current) => {
      const next = new Set(current)
      if (next.has(jobNumber)) next.delete(jobNumber)
      else next.add(jobNumber)
      return next
    })
  }

  function exportCsv() {
    const csvRows = sortedRows.map((row) => {
      const statusCols = Object.fromEntries(row.distribution.map((d) => [`${d.status} Count`, d.count]))
      return {
        'Job Number': row.jobNumber,
        'Tract Count': row.tractCount,
        ...statusCols,
        'Attorneys Assigned': row.attorneys.join(', '),
        'Earliest Upcoming Event Date': row.nextHard ? formatDate(row.nextHard.date) : '',
        'Earliest Upcoming Event Label': row.nextHard?.label || '',
        'Total Estimate of Just Compensation Deposited': row.totalDeposit,
      }
    })
    downloadCsv(`By_Job_${new Date().toISOString().slice(0, 10)}.csv`, csvRows)
  }

  if (allCases.length === 0) {
    return <EmptyState title="No tracts on file." description="Tracts appear here once cases exist for the division." />
  }

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: '0.6rem' }}>
        <Btn onClick={exportCsv}>Export CSV</Btn>
      </div>
      <StatusDistributionLegend />
      <div className="table-wrap">
        <table className="ui-table compact-table">
          <thead>
            <tr>
              <th></th>
              {COLUMNS.map((column) => (
                <th
                  key={column.key}
                  className="sortable-header"
                  onClick={() => toggleSort(column.key)}
                  aria-sort={sortColumn === column.key ? (sortDirection === 'asc' ? 'ascending' : 'descending') : undefined}
                >
                  {column.label}
                  {sortColumn === column.key && <span className="sort-indicator">{sortDirection === 'asc' ? ' ▲' : ' ▼'}</span>}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {sortedRows.map((row) => {
              const isExpanded = expanded.has(row.jobNumber)
              return (
                <Fragment key={row.jobNumber}>
                  <tr className="clickable-row" onClick={() => toggleExpanded(row.jobNumber)}>
                    <td>
                      <button type="button" className="link-button" aria-label={isExpanded ? 'Collapse' : 'Expand'}>
                        {isExpanded ? '▾' : '▸'}
                      </button>
                    </td>
                    <td>{row.jobNumber}</td>
                    <td>{row.tractCount}</td>
                    <td><StatusDistributionBar counts={row.distribution} /></td>
                    <td>
                      {row.attorneys.map((attorney) => (
                        <span key={attorney} className="pill pill-neutral" style={{ marginRight: '0.25rem', marginBottom: '0.2rem' }}>{attorney}</span>
                      ))}
                    </td>
                    <td>{row.nextHard ? <>{formatDate(row.nextHard.date)}<div className="subtle-text">{row.nextHard.label}</div></> : '—'}</td>
                    <td>{formatCurrencyOrDash(row.totalDeposit)}</td>
                  </tr>
                  {isExpanded && (
                    <tr>
                      <td colSpan={COLUMNS.length + 1} style={{ padding: 0 }}>
                        <div className="table-wrap" style={{ margin: '0.35rem 0.6rem 0.6rem' }}>
                          <table className="ui-table compact-table">
                            <thead>
                              <tr>
                                <th>Job + Tract</th>
                                <th>Case Name</th>
                                <th>Attorney</th>
                                <th>Status</th>
                                <th>Next Hard Date</th>
                                <th></th>
                              </tr>
                            </thead>
                            <tbody>
                              {row.cases.map((tract) => {
                                const tractNextHard = nextHardDate([tract], hearings)
                                return (
                                  <tr key={tract.id}>
                                    <td>{[tract.jobNumber, tract.tract].filter(Boolean).join(' · ') || '—'}</td>
                                    <td>{tract.caseName}</td>
                                    <td>{tract.assignedAttorney || 'Unassigned'}</td>
                                    <td>{tract.caseStatus || 'Pipeline'}</td>
                                    <td>{tractNextHard ? <>{formatDate(tractNextHard.date)}<div className="subtle-text">{tractNextHard.label}</div></> : '—'}</td>
                                    <td><Btn size="sm" onClick={() => onOpenCase(tract.id)}>Open Case</Btn></td>
                                  </tr>
                                )
                              })}
                            </tbody>
                          </table>
                        </div>
                      </td>
                    </tr>
                  )}
                </Fragment>
              )
            })}
          </tbody>
        </table>
      </div>
    </div>
  )
}
