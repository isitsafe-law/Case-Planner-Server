import { useMemo, useState } from 'react'
import type { TrialWatchEntry } from './types'
import { EmptyState } from './EmptyState'
import { formatDate } from '../ui/format'

type TrialHorizon = 90 | 180 | 365 | 'all'

export function TrialWatchTable({ entries, onOpenCase }: { entries: TrialWatchEntry[]; onOpenCase: (caseId: number) => void }) {
  const [horizon, setHorizon] = useState<TrialHorizon>(180)
  const [showAll, setShowAll] = useState(false)

  const ordered = useMemo(() => [...entries]
    .filter((entry) => horizon === 'all' || (entry.daysUntilTrial != null && entry.daysUntilTrial >= 0 && entry.daysUntilTrial <= horizon))
    .sort((a, b) => (a.daysUntilTrial ?? 99999) - (b.daysUntilTrial ?? 99999)), [entries, horizon])
  const visible = showAll ? ordered : ordered.slice(0, 8)

  if (entries.length === 0) {
    return <EmptyState title="No trial-track matters" description="This section appears once a case is marked Trial Track or has a trial date within the watch window." />
  }

  return (
    <div className="trial-watch-list">
      <div className="inline-quick-form" style={{ marginBottom: '0.65rem' }}>
        <span className="helper-text">{ordered.length} trial{ordered.length === 1 ? '' : 's'} in view</span>
        <label>
          <span className="visually-hidden">Trial horizon</span>
          <select value={horizon} onChange={(event) => { setHorizon(event.target.value === 'all' ? 'all' : Number(event.target.value) as TrialHorizon); setShowAll(false) }}>
            <option value={90}>Next 90 days</option>
            <option value={180}>Next 180 days</option>
            <option value={365}>Next 365 days</option>
            <option value="all">All in watch window</option>
          </select>
        </label>
      </div>

      {ordered.length === 0 ? (
        <EmptyState title="No trials in this horizon" description="Choose a longer horizon or use Calendar for the complete trial schedule." />
      ) : (
        <>
          {visible.map((trial) => {
            const days = trial.daysUntilTrial
            const warning = days !== null && days <= 30 ? 'Immediate attention' : days !== null && days <= 90 ? 'Coming up' : 'Scheduled'
            return <article key={trial.caseId} className="trial-watch-card" onClick={() => onOpenCase(trial.caseId)}>
              <div className="trial-watch-card-header"><div><strong>{trial.caseName}</strong>{trial.caseNumber && <span className="subtle-text"> - {trial.caseNumber}</span>}</div><span className={`pill ${days !== null && days <= 30 ? 'pill-warn' : 'pill-neutral'}`}>{warning}</span></div>
              <div className="trial-watch-primary"><div><span>Trial date</span><strong>{formatDate(trial.trialDate)}</strong></div><div><span>Days until trial</span><strong>{days ?? '-'}</strong></div><div><span>Current status</span><strong>{trial.discoveryStatus || 'Trial preparation'}</strong></div></div>
              <div className="trial-watch-next"><span>Next required action</span><strong>{trial.nextTrialDecision || 'Review trial preparation plan'}</strong></div>
              <button onClick={(event) => { event.stopPropagation(); onOpenCase(trial.caseId) }}>Open Case</button>
            </article>
          })}
          {ordered.length > visible.length && <button className="link-button" onClick={() => setShowAll(true)}>Show {ordered.length - visible.length} more trials</button>}
          {showAll && ordered.length > 8 && <button className="link-button" onClick={() => setShowAll(false)}>Show fewer trials</button>}
        </>
      )}
    </div>
  )
}
