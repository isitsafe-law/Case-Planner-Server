import { Fragment, useMemo, useState } from 'react'
import type { AuthenticatedUserProfile, CaseRecord } from '../App'
import { api } from '../App'
import { Btn } from '../ui/Btn'
import { downloadCsv } from '../ui/csvExport'
import { formatDateTime } from '../ui/format'
import { EmptyState } from './EmptyState'
import { SettlementAuthorityDecisionDialog, type SettlementAuthorityDecisionAction } from './SettlementAuthorityDecisionDialog'
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

// Chief Counsel-only gate for the decide action, mirroring the server's own IsChiefCounsel check
// (Program.cs) and this app's universal "no currentUser = local/no-auth, unrestricted" convention
// (see the `(!currentUser || currentUser.isAdmin || currentUser.isManager)` precedent in App.tsx).
export function canDecideSettlementAuthority(currentUser: AuthenticatedUserProfile | null): boolean {
  return !currentUser || currentUser.managerTier === 'ChiefCounsel'
}

type JoinedRequest = {
  request: SettlementAuthorityRequestRecord
  matchedCase: CaseRecord | undefined
}

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
  currentUser,
  onOpenCase,
  onDecided,
}: {
  allCases: CaseRecord[]
  settlementAuthorityRequests: SettlementAuthorityRequestRecord[]
  currentUser: AuthenticatedUserProfile | null
  onOpenCase: (caseId: number) => void
  onDecided: () => Promise<void>
}) {
  const [dialogState, setDialogState] = useState<{ request: SettlementAuthorityRequestRecord; action: SettlementAuthorityDecisionAction } | null>(null)
  const [expandedIds, setExpandedIds] = useState<Set<number>>(new Set())
  const [errorMessage, setErrorMessage] = useState('')
  const canDecide = canDecideSettlementAuthority(currentUser)

  const caseById = useMemo(() => new Map(allCases.map((c) => [c.id, c])), [allCases])

  // Sort: actionable requests (Pending/InfoRequested) first so Chief Counsel never has to scroll
  // past already-decided history to find what still needs a decision; oldest-first within that
  // group so nothing sits neglected. Already-decided requests follow, most-recently-decided first.
  const ordered = useMemo(() => {
    function isActionable(r: SettlementAuthorityRequestRecord) {
      return r.status === 'Pending' || r.status === 'InfoRequested'
    }
    return [...settlementAuthorityRequests].sort((a, b) => {
      const aActionable = isActionable(a)
      const bActionable = isActionable(b)
      if (aActionable !== bActionable) return aActionable ? -1 : 1
      if (aActionable) return a.requestedAt.localeCompare(b.requestedAt)
      return (b.decidedAt || '').localeCompare(a.decidedAt || '')
    })
  }, [settlementAuthorityRequests])

  const joined: JoinedRequest[] = useMemo(
    () => ordered.map((request) => ({ request, matchedCase: caseById.get(request.caseId) })),
    [ordered, caseById],
  )

  function toggleExpanded(id: number) {
    setExpandedIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  async function submitDecision(payload: { action: SettlementAuthorityDecisionAction; comment: string; grantedAmount?: number }) {
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
    const rows = joined.map(({ request, matchedCase }) => {
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
        'Decided At': request.decidedAt ? formatDateTime(request.decidedAt) : '',
        'Decided By': request.decidedByDisplay || '',
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
      {!canDecide && (
        <p className="helper-text" style={{ marginBottom: '0.6rem' }}>
          Only Chief Counsel can decide a Settlement Authority request. You have read access to this queue.
        </p>
      )}
      {errorMessage && <p className="helper-text" style={{ color: 'var(--danger, #b3261e)' }}>{errorMessage}</p>}
      <div className="table-wrap">
        <table className="ui-table compact-table">
          <thead>
            <tr>
              <th>Job + Tract</th>
              <th>Requested Amount</th>
              <th>Est. of Just Compensation Deposit</th>
              <th>Delta</th>
              <th>Requesting Attorney</th>
              <th>Days Pending</th>
              <th>Status</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {joined.map(({ request, matchedCase }) => {
              const delta = settlementAuthorityDelta(request.requestedAmount, matchedCase?.depositAmount)
              const deltaPercent = settlementAuthorityDeltaPercent(request.requestedAmount, matchedCase?.depositAmount)
              const canAct = request.status === 'Pending' || request.status === 'InfoRequested'
              const isExpanded = expandedIds.has(request.id)
              const hasHistory = request.decidedAt || request.decisionComment
              return (
                <Fragment key={request.id}>
                  <tr>
                    <td>{[matchedCase?.jobNumber, matchedCase?.tract].filter(Boolean).join(' · ') || '—'}</td>
                    <td>{formatCurrency(request.requestedAmount)}</td>
                    <td>{formatCurrency(matchedCase?.depositAmount)}</td>
                    <td>{formatCurrency(delta)} {delta != null && <span className="subtle-text">({formatPercent(deltaPercent)})</span>}</td>
                    <td>{request.requestingAttorney || matchedCase?.assignedAttorney || '—'}</td>
                    <td>{request.status === 'Pending' ? daysPending(request.requestedAt) : '—'}</td>
                    <td>
                      <span className={`pill ${STATUS_PILL_CLASS[request.status]}`}>{STATUS_LABEL[request.status]}</span>
                      {hasHistory && (
                        <button type="button" className="link-button" style={{ display: 'block', marginTop: '0.2rem' }} onClick={() => toggleExpanded(request.id)}>
                          {isExpanded ? 'Hide history' : 'Show history'}
                        </button>
                      )}
                    </td>
                    <td>
                      <div className="button-row compact-actions">
                        <Btn
                          size="sm"
                          disabled={!canAct || !canDecide}
                          title={!canDecide ? 'Only Chief Counsel can decide this request' : undefined}
                          onClick={() => setDialogState({ request, action: 'Approved' })}
                        >
                          Approve/Grant
                        </Btn>
                        <Btn
                          size="sm"
                          variant="danger"
                          disabled={!canAct || !canDecide}
                          title={!canDecide ? 'Only Chief Counsel can decide this request' : undefined}
                          onClick={() => setDialogState({ request, action: 'Denied' })}
                        >
                          Deny
                        </Btn>
                        <Btn
                          size="sm"
                          variant="ghost"
                          disabled={!canAct || !canDecide}
                          title={!canDecide ? 'Only Chief Counsel can decide this request' : undefined}
                          onClick={() => setDialogState({ request, action: 'InfoRequested' })}
                        >
                          Request Info
                        </Btn>
                        {matchedCase && <Btn size="sm" variant="ghost" onClick={() => onOpenCase(matchedCase.id)}>Open Case</Btn>}
                      </div>
                    </td>
                  </tr>
                  {isExpanded && hasHistory && (
                    <tr>
                      <td colSpan={8} className="ui-week-row" style={{ textAlign: 'left' }}>
                        <strong>Decision history:</strong>{' '}
                        {request.decidedAt ? formatDateTime(request.decidedAt) : '—'}
                        {request.decidedByDisplay ? ` · ${request.decidedByDisplay}` : ''}
                        {request.decisionComment ? ` — “${request.decisionComment}”` : ''}
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
