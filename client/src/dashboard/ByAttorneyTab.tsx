import { Fragment, useMemo, useState } from 'react'
import type { CaseRecord, Hearing } from '../App'
import { Btn } from '../ui/Btn'
import { downloadCsv } from '../ui/csvExport'
import { formatDate } from '../ui/format'
import { EmptyState } from './EmptyState'
import { needsAttention } from './ManagerDashboard'
import { nextHardDate, statusDistribution, type NextHardDate, type StatusCount } from './dashboardAggregation'
import { StatusDistributionBar, StatusDistributionLegend } from './StatusDistributionBar'
import type { SettlementAuthorityRequestRecord } from './types'

// Note: this tab deliberately does not use ManagerDashboard.tsx's exported isOpenForDivision - the
// "tract counts by status" bar and every other total here intentionally span all six caseStatus
// values, including Resolved / Closed, per the spec's "all six caseStatus values" requirement. Only
// needsAttention() is reused, for the "needs-attention count" column below.

export type AttorneyRow = {
  attorney: string
  cases: CaseRecord[]
  distribution: StatusCount[]
  totalTracts: number
  nextHard: NextHardDate | null
  needsAttentionCount: number
  pendingApprovalsCount: number
}

// Groups allCases by assignedAttorney (blank/missing -> "Unassigned", matching
// IncomingPipelinePanel.tsx's own convention) and computes each column's value for every group.
// Exported for unit testing independent of the rendered table.
export function buildAttorneyRows(allCases: CaseRecord[], hearings: Hearing[], settlementAuthorityRequests: SettlementAuthorityRequestRecord[]): AttorneyRow[] {
  const groups = new Map<string, CaseRecord[]>()
  for (const record of allCases) {
    const attorney = record.assignedAttorney || 'Unassigned'
    if (!groups.has(attorney)) groups.set(attorney, [])
    groups.get(attorney)!.push(record)
  }

  const attorneyByCaseId = new Map(allCases.map((record) => [record.id, record.assignedAttorney || 'Unassigned']))
  const pendingByAttorney = new Map<string, number>()
  for (const request of settlementAuthorityRequests) {
    if (request.status !== 'Pending') continue
    const attorney = attorneyByCaseId.get(request.caseId)
    if (!attorney) continue
    pendingByAttorney.set(attorney, (pendingByAttorney.get(attorney) || 0) + 1)
  }

  return Array.from(groups.entries()).map(([attorney, cases]) => ({
    attorney,
    cases,
    distribution: statusDistribution(cases),
    totalTracts: cases.length,
    nextHard: nextHardDate(cases, hearings),
    needsAttentionCount: cases.filter(needsAttention).length,
    pendingApprovalsCount: pendingByAttorney.get(attorney) || 0,
  }))
}

type AttorneySortColumn = 'attorney' | 'tracts' | 'nextHardDate' | 'needsAttention' | 'pendingApprovals'

// Sortable on every column per spec. The stacked-bar "tract counts by status" column has no single
// obvious sort key of its own, so it sorts by total tract count (a judgment call, documented here
// rather than left implicit). Undated "next hard date" rows always sort last regardless of
// direction - matches IncomingPipelinePanel.tsx's own dateSentToCurrentHolder sort convention.
export function sortAttorneyRows(rows: AttorneyRow[], column: AttorneySortColumn, direction: 'asc' | 'desc'): AttorneyRow[] {
  const dir = direction === 'asc' ? 1 : -1
  return [...rows].sort((a, b) => {
    switch (column) {
      case 'attorney':
        return dir * a.attorney.localeCompare(b.attorney)
      case 'tracts':
        return dir * (a.totalTracts - b.totalTracts)
      case 'needsAttention':
        return dir * (a.needsAttentionCount - b.needsAttentionCount)
      case 'pendingApprovals':
        return dir * (a.pendingApprovalsCount - b.pendingApprovalsCount)
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

const COLUMNS: { key: AttorneySortColumn; label: string }[] = [
  { key: 'attorney', label: 'Attorney' },
  { key: 'tracts', label: 'Tract Counts by Status' },
  { key: 'nextHardDate', label: 'Next Hard Date' },
  { key: 'needsAttention', label: 'Needs Attention' },
  { key: 'pendingApprovals', label: 'Pending Approvals' },
]

export function ByAttorneyTab({
  allCases,
  hearings,
  settlementAuthorityRequests,
  onOpenCase,
}: {
  allCases: CaseRecord[]
  hearings: Hearing[]
  settlementAuthorityRequests: SettlementAuthorityRequestRecord[]
  onOpenCase: (caseId: number) => void
}) {
  const [sortColumn, setSortColumn] = useState<AttorneySortColumn>('attorney')
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('asc')
  const [expanded, setExpanded] = useState<Set<string>>(new Set())

  const rows = useMemo(() => buildAttorneyRows(allCases, hearings, settlementAuthorityRequests), [allCases, hearings, settlementAuthorityRequests])
  const sortedRows = useMemo(() => sortAttorneyRows(rows, sortColumn, sortDirection), [rows, sortColumn, sortDirection])

  function toggleSort(column: AttorneySortColumn) {
    if (sortColumn === column) {
      setSortDirection((current) => (current === 'asc' ? 'desc' : 'asc'))
    } else {
      setSortColumn(column)
      setSortDirection('asc')
    }
  }

  function toggleExpanded(attorney: string) {
    setExpanded((current) => {
      const next = new Set(current)
      if (next.has(attorney)) next.delete(attorney)
      else next.add(attorney)
      return next
    })
  }

  function exportCsv() {
    const csvRows = sortedRows.map((row) => {
      const statusCols = Object.fromEntries(row.distribution.map((d) => [`${d.status} Count`, d.count]))
      return {
        Attorney: row.attorney,
        ...statusCols,
        'Next Hard Date': row.nextHard ? formatDate(row.nextHard.date) : '',
        'Next Hard Date Label': row.nextHard?.label || '',
        'Needs Attention Count': row.needsAttentionCount,
        'Pending Approvals Count': row.pendingApprovalsCount,
      }
    })
    downloadCsv(`By_Attorney_${new Date().toISOString().slice(0, 10)}.csv`, csvRows)
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
              const isExpanded = expanded.has(row.attorney)
              return (
                <Fragment key={row.attorney}>
                  <tr className="clickable-row" onClick={() => toggleExpanded(row.attorney)}>
                    <td>
                      <button type="button" className="link-button" aria-label={isExpanded ? 'Collapse' : 'Expand'}>
                        {isExpanded ? '▾' : '▸'}
                      </button>
                    </td>
                    <td>{row.attorney}</td>
                    <td><StatusDistributionBar counts={row.distribution} /></td>
                    <td>{row.nextHard ? <>{formatDate(row.nextHard.date)}<div className="subtle-text">{row.nextHard.label}</div></> : '—'}</td>
                    <td>{row.needsAttentionCount}</td>
                    <td>{row.pendingApprovalsCount}</td>
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
                                <th>Status</th>
                                <th>Next Hard Date</th>
                                <th>Needs Attention</th>
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
                                    <td>{tract.caseStatus || 'Pipeline'}</td>
                                    <td>{tractNextHard ? <>{formatDate(tractNextHard.date)}<div className="subtle-text">{tractNextHard.label}</div></> : '—'}</td>
                                    <td>{needsAttention(tract) ? <span className="pill pill-warn">Needs attention</span> : '—'}</td>
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
