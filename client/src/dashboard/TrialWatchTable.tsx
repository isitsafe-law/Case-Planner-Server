import type { TrialWatchEntry } from './types'
import { EmptyState } from './EmptyState'

export function TrialWatchTable({ entries, onOpenCase }: { entries: TrialWatchEntry[]; onOpenCase: (caseId: number) => void }) {
  if (entries.length === 0) {
    return <EmptyState title="No trial-track matters" description="This section appears once a case is marked Trial Track or has a trial date within the watch window." />
  }

  const ordered = [...entries].sort((a, b) => (a.daysUntilTrial ?? 99999) - (b.daysUntilTrial ?? 99999))
  return (
    <div className="trial-watch-list">
      {ordered.map((t) => {
        const days = t.daysUntilTrial
        const warning = days !== null && days < 0 ? 'Past due' : days !== null && days <= 30 ? 'Immediate attention' : days !== null && days <= 90 ? 'Coming up' : 'Scheduled'
        return <article key={t.caseId} className="trial-watch-card" onClick={() => onOpenCase(t.caseId)}>
          <div className="trial-watch-card-header"><div><strong>{t.caseName}</strong>{t.caseNumber && <span className="subtle-text"> · {t.caseNumber}</span>}</div><span className={`pill ${days !== null && days <= 30 ? 'pill-warn' : 'pill-neutral'}`}>{warning}</span></div>
          <div className="trial-watch-primary"><div><span>Trial date</span><strong>{t.trialDate ?? 'Not set'}</strong></div><div><span>Days until trial</span><strong>{days ?? '—'}</strong></div><div><span>Current status</span><strong>{t.discoveryStatus || 'Trial preparation'}</strong></div></div>
          <div className="trial-watch-next"><span>Next required action</span><strong>{t.nextTrialDecision || 'Review trial preparation plan'}</strong></div>
          <button onClick={(event) => { event.stopPropagation(); onOpenCase(t.caseId) }}>Open Case</button>
        </article>
      })}
    </div>
  )
}
