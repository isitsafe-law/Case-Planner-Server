import { Fragment, useMemo, useState } from 'react'
import type { CaseRecord } from '../App'
import { api } from '../App'
import { Btn } from '../ui/Btn'
import { downloadCsv } from '../ui/csvExport'
import { formatDate, formatDateTime } from '../ui/format'
import { EmptyState } from './EmptyState'
import { SettlementAuthorityDecisionDialog, type SettlementAuthorityDecisionAction, type SettlementAuthorityDecisionPayload } from './SettlementAuthorityDecisionDialog'
import type { SettlementAuthorityRequestRecord } from './types'

// Pure math helpers, exported for unit testing (see __tests__/SettlementAuthoritySection.test.tsx).
// depositAmount being null/zero/undefined is the normal state for a tract with no Estimate of Just
// Compensation deposit recorded yet - never divide, just show "no basis for comparison" (null).
export function settlementAuthorityDelta(requestedAmount: number, depositAmount?: number | null): number | null {
  if (depositAmount == null) return null
  return requestedAmount - depositAmount
}

export function settlementAuthorityDeltaPercent(requestedAmount: number, depositAmount?: number | null): number | null {
  if (!depositAmount) return null
  return ((requestedAmount - depositAmount) / depositAmount) * 100
}

// Whole days from requestedAt to `now` (default: actual now) - only meaningful while a request is
// still Pending, per ApprovalsTab's spec; callers show decided-date info instead for every other
// status. Floors rather than rounds so "just requested a few hours ago" reads as 0, not 1.
export function daysPending(requestedAt: string, now: Date = new Date()): number {
  const requested = new Date(requestedAt)
  const diffMs = now.getTime() - requested.getTime()
  return Math.max(0, Math.floor(diffMs / 86_400_000))
}

// Local formatter distinct from App.tsx's own (unexported) displayCurrency, which renders a null
// amount as "Not set" - this feature's spec calls for "—" on anything with no basis for comparison,
// matching every other dashboard cell's blank-value convention (see formatDate's own "—" default).
function formatCurrency(value?: number | null): string {
  if (value == null) return '—'
  return `$${value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function formatPercent(value: number | null): string {
  if (value == null) return '—'
  return `${value >= 0 ? '+' : ''}${value.toFixed(1)}%`
}

type JoinedRequest = {
  request: SettlementAuthorityRequestRecord
  matchedCase: CaseRecord | undefined
}

// Manager Dashboard sign-off consolidation, item 4: Settlement Authority is pure record-keeping now
// - recording an outcome requires only ordinary case-write access (enforced server-side), not a
// specific role, so this section is a sortable log rather than a decision inbox gated to one
// person. Sortable on every visible data column except Actions, mirroring ByAttorneyTab.tsx's
// toggleSort/COLUMNS/sortable-header convention. Undated "days pending"/"decided at" values always
// sort last regardless of direction, matching that same file's undated-date convention.
type SettlementAuthoritySortColumn = 'jobTract' | 'requestedAmount' | 'delta' | 'requestingAttorney' | 'requestedAt' | 'status' | 'decidedAt'

function jobTractLabel(matchedCase: CaseRecord | undefined): string {
  return [matchedCase?.jobNumber, matchedCase?.tract].filter(Boolean).join(' · ')
}

export function sortSettlementAuthorityRows(rows: JoinedRequest[], column: SettlementAuthoritySortColumn, direction: 'asc' | 'desc'): JoinedRequest[] {
  const dir = direction === 'asc' ? 1 : -1
  return [...rows].sort((a, b) => {
    switch (column) {
      case 'jobTract':
        return dir * jobTractLabel(a.matchedCase).localeCompare(jobTractLabel(b.matchedCase))
      case 'requestedAmount':
        return dir * (a.request.requestedAmount - b.request.requestedAmount)
      case 'delta': {
        const deltaA = settlementAuthorityDelta(a.request.requestedAmount, a.matchedCase?.depositAmount)
        const deltaB = settlementAuthorityDelta(b.request.requestedAmount, b.matchedCase?.depositAmount)
        if (deltaA == null && deltaB == null) return 0
        if (deltaA == null) return 1
        if (deltaB == null) return -1
        return dir * (deltaA - deltaB)
      }
      case 'requestingAttorney':
        return dir * (a.request.requestingAttorney || a.matchedCase?.assignedAttorney || '').localeCompare(b.request.requestingAttorney || b.matchedCase?.assignedAttorney || '')
      case 'requestedAt':
        return dir * a.request.requestedAt.localeCompare(b.request.requestedAt)
      case 'status':
        return dir * a.request.status.localeCompare(b.request.status)
      case 'decidedAt': {
        if (!a.request.decidedAt && !b.request.decidedAt) return 0
        if (!a.request.decidedAt) return 1
        if (!b.request.decidedAt) return -1
        return dir * a.request.decidedAt.localeCompare(b.request.decidedAt)
      }
      default:
        return 0
    }
  })
}

const COLUMNS: { key: SettlementAuthoritySortColumn; label: string }[] = [
  { key: 'jobTract', label: 'Job + Tract' },
  { key: 'requestedAmount', label: 'Requested Amount' },
  { key: 'delta', label: 'Delta vs. Deposit' },
  { key: 'requestingAttorney', label: 'Requesting Attorney' },
  { key: 'requestedAt', label: 'Requested / Days Pending' },
  { key: 'status', label: 'Status' },
  { key: 'decidedAt', label: 'Recorded' },
]

const STATUS_PILL_CLASS: Record<SettlementAuthorityRequestRecord['status'], string> = {
  Pending: 'pill-warn',
  Approved: 'pill-success',
  Denied: 'pill-danger',
  InfoRequested: 'pill-neutral',
}

const STATUS_LABEL: Record<SettlementAuthorityRequestRecord['status'], string> = {
  Pending: 'Pending',
  Approved: 'Approved',
  Denied: 'Denied',
  InfoRequested: 'Info Requested',
}

export function SettlementAuthoritySection({
  allCases,
  settlementAuthorityRequests,
  onOpenCase,
  onDecided,
}: {
  allCases: CaseRecord[]
  settlementAuthorityRequests: SettlementAuthorityRequestRecord[]
  onOpenCase: (caseId: number) => void
  onDecided: () => Promise<void>
}) {
  const [dialogState, setDialogState] = useState<{ request: SettlementAuthorityRequestRecord; action: SettlementAuthorityDecisionAction } | null>(null)
  const [expandedIds, setExpandedIds] = useState<Set<number>>(new Set())
  const [errorMessage, setErrorMessage] = useState('')
  const [sortColumn, setSortColumn] = useState<SettlementAuthoritySortColumn>('requestedAt')
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('desc')

  const caseById = useMemo(() => new Map(allCases.map((c) => [c.id, c])), [allCases])

  const joined: JoinedRequest[] = useMemo(
    () => settlementAuthorityRequests.map((request) => ({ request, matchedCase: caseById.get(request.caseId) })),
    [settlementAuthorityRequests, caseById],
  )
  const sortedRows = useMemo(() => sortSettlementAuthorityRows(joined, sortColumn, sortDirection), [joined, sortColumn, sortDirection])

  function toggleSort(column: SettlementAuthoritySortColumn) {
    if (sortColumn === column) {
      setSortDirection((current) => (current === 'asc' ? 'desc' : 'asc'))
    } else {
      setSortColumn(column)
      setSortDirection('asc')
    }
  }

  function toggleExpanded(id: number) {
    setExpandedIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  async function submitDecision(payload: SettlementAuthorityDecisionPayload) {
    if (!dialogState) return
    try {
      setErrorMessage('')
      await api(`/api/settlement-authority-requests/${dialogState.request.id}/decide`, {
        method: 'POST',
        body: JSON.stringify(payload),
      })
      setDialogState(null)
      await onDecided()
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Unable to record this decision.')
    }
  }

  function exportCsv() {
    const rows = sortedRows.map(({ request, matchedCase }) => {
      const delta = settlementAuthorityDelta(request.requestedAmount, matchedCase?.depositAmount)
      const deltaPercent = settlementAuthorityDeltaPercent(request.requestedAmount, matchedCase?.depositAmount)
      return {
        'Job Number': matchedCase?.jobNumber || '',
        Tract: matchedCase?.tract || '',
        'Case Name': matchedCase?.caseName || '',
        'Requested Amount': request.requestedAmount,
        'Estimate of Just Compensation Deposit': matchedCase?.depositAmount ?? '',
        Delta: delta ?? '',
        'Delta %': deltaPercent != null ? deltaPercent.toFixed(1) : '',
        'Requesting Attorney': request.requestingAttorney || matchedCase?.assignedAttorney || '',
        'Days Pending': request.status === 'Pending' ? daysPending(request.requestedAt) : '',
        Status: STATUS_LABEL[request.status],
        'Recorded At': request.decidedAt ? formatDateTime(request.decidedAt) : '',
        'Recorded By': request.decidedByDisplay || '',
        'Granted By': request.grantedBy || '',
        'Granted By Role': request.grantedByRole || '',
        'Granted Date': request.grantedDate ? formatDate(request.grantedDate) : '',
        'Document Reference': request.documentReference || '',
        'Decision Comment': request.decisionComment || '',
      }
    })
    downloadCsv(`Settlement_Authority_Requests_${new Date().toISOString().slice(0, 10)}.csv`, rows)
  }

  if (joined.length === 0) {
    return <EmptyState title="No Settlement Authority requests on file." description="Requests appear here once an attorney asks for Settlement Authority above the case's Estimate of Just Compensation." />
  }

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: '0.6rem' }}>
        <Btn onClick={exportCsv}>Export CSV</Btn>
      </div>
      {errorMessage && <p className="helper-text" style={{ color: 'var(--danger, #b3261e)' }}>{errorMessage}</p>}
      <div className="table-wrap">
        <table className="ui-table compact-table">
          <thead>
            <tr>
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
              <th>Est. of Just Compensation Deposit</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {sortedRows.map(({ request, matchedCase }) => {
              const delta = settlementAuthorityDelta(request.requestedAmount, matchedCase?.depositAmount)
              const deltaPercent = settlementAuthorityDeltaPercent(request.requestedAmount, matchedCase?.depositAmount)
              const canAct = request.status === 'Pending' || request.status === 'InfoRequested'
              const isExpanded = expandedIds.has(request.id)
              const hasHistory = request.decidedAt || request.decisionComment
              return (
                <Fragment key={request.id}>
                  <tr>
                    <td>{jobTractLabel(matchedCase) || '—'}</td>
                    <td>{formatCurrency(request.requestedAmount)}</td>
                    <td>{formatCurrency(delta)} {delta != null && <span className="subtle-text">({formatPercent(deltaPercent)})</span>}</td>
                    <td>{request.requestingAttorney || matchedCase?.assignedAttorney || '—'}</td>
                    <td>{request.status === 'Pending' ? daysPending(request.requestedAt) : formatDate(request.requestedAt)}</td>
                    <td>
                      <span className={`pill ${STATUS_PILL_CLASS[request.status]}`}>{STATUS_LABEL[request.status]}</span>
                      {hasHistory && (
                        <button type="button" className="link-button" style={{ display: 'block', marginTop: '0.2rem' }} onClick={() => toggleExpanded(request.id)}>
                          {isExpanded ? 'Hide history' : 'Show history'}
                        </button>
                      )}
                    </td>
                    <td>{request.decidedAt ? formatDate(request.decidedAt) : '—'}</td>
                    <td>{formatCurrency(matchedCase?.depositAmount)}</td>
                    <td>
                      <div className="button-row compact-actions">
                        <Btn size="sm" disabled={!canAct} onClick={() => setDialogState({ request, action: 'Approved' })}>
                          Record Grant
                        </Btn>
                        <Btn size="sm" variant="danger" disabled={!canAct} onClick={() => setDialogState({ request, action: 'Denied' })}>
                          Record Denial
                        </Btn>
                        <Btn size="sm" variant="ghost" disabled={!canAct} onClick={() => setDialogState({ request, action: 'InfoRequested' })}>
                          Record Info Requested
                        </Btn>
                        {matchedCase && <Btn size="sm" variant="ghost" onClick={() => onOpenCase(matchedCase.id)}>Open Case</Btn>}
                      </div>
                    </td>
                  </tr>
                  {isExpanded && hasHistory && (
                    <tr>
                      <td colSpan={9} className="ui-week-row" style={{ textAlign: 'left' }}>
                        <strong>Recorded:</strong>{' '}
                        {request.decidedAt ? formatDateTime(request.decidedAt) : '—'}
                        {request.decidedByDisplay ? ` · ${request.decidedByDisplay}` : ''}
                        {request.decisionComment ? ` — “${request.decisionComment}”` : ''}
                        {request.status === 'Approved' && (
                          <>
                            <br />
                            <strong>Granted:</strong>{' '}
                            {request.grantedDate ? formatDate(request.grantedDate) : '—'}
                            {request.grantedBy ? ` · ${request.grantedBy}` : ''}
                            {request.grantedByRole ? ` (${request.grantedByRole})` : ''}
                          </>
                        )}
                        {request.documentReference && (
                          <>
                            <br />
                            <strong>Reference:</strong> {request.documentReference}
                          </>
                        )}
                      </td>
                    </tr>
                  )}
                </Fragment>
              )
            })}
          </tbody>
        </table>
      </div>

      {dialogState && (
        <SettlementAuthorityDecisionDialog
          request={dialogState.request}
          caseLabel={caseById.get(dialogState.request.caseId)?.caseName || `Case ${dialogState.request.caseId}`}
          action={dialogState.action}
          onClose={() => setDialogState(null)}
          onSubmit={submitDecision}
        />
      )}
    </div>
  )
}
