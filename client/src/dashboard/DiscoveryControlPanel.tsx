import { useState } from 'react'
import type { DiscoveryControlSummary } from './types'
import { EmptyState } from './EmptyState'

const CONDITION_ORDER: { key: keyof DiscoveryControlSummary; label: string }[] = [
  { key: 'strategyNotSelected', label: 'Strategy not selected' },
  { key: 'strategySelectedNotServed', label: 'Strategy selected but discovery not served' },
  { key: 'responsesOverdue', label: 'Responses overdue' },
  { key: 'responsesReceivedNotReviewed', label: 'Responses received but not reviewed' },
  { key: 'deficienciesUnresolved', label: 'Deficiencies unresolved' },
  { key: 'depositionDecisionPending', label: 'Deposition decision pending' },
  { key: 'cutoffApproaching', label: 'Discovery cutoff approaching' },
  { key: 'complete', label: 'Discovery complete' },
  { key: 'noDiscoveryNeeded', label: 'No discovery currently needed' },
]

export function DiscoveryControlPanel({ summary, onOpenCase }: { summary: DiscoveryControlSummary; onOpenCase: (caseId: number) => void }) {
  const [expanded, setExpanded] = useState<string | null>(null)
  const total = CONDITION_ORDER.reduce((sum, c) => sum + (summary[c.key] as number), 0)

  if (total === 0) {
    return <EmptyState title="No filed cases yet" description="Discovery conditions will appear here once cases are filed." />
  }

  return (
    <div className="discovery-control-panel">
      <div className="discovery-condition-grid">
        {CONDITION_ORDER.map(({ key, label }) => {
          const count = summary[key] as number
          return (
            <button
              key={key}
              type="button"
              className={`discovery-condition-chip${expanded === label ? ' active' : ''}`}
              onClick={() => setExpanded(expanded === label ? null : label)}
              disabled={count === 0}
              aria-pressed={expanded === label}
            >
              <span>{label}</span>
              <strong>{count}</strong>
            </button>
          )
        })}
      </div>
      {expanded && (
        <ul className="discovery-condition-detail">
          {(summary.casesByCondition[expanded] ?? []).map((c) => (
            <li key={c.caseId}>
              <button onClick={() => onOpenCase(c.caseId)}>{c.caseName}{c.caseNumber ? ` (${c.caseNumber})` : ''}</button>
              {c.nextDecision && <span className="subtle-text"> - {c.nextDecision}</span>}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
