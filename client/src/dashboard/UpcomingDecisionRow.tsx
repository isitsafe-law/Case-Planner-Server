import type { UpcomingDecisionItem } from './types'

export function UpcomingDecisionRow({ decision, onOpenCase }: { decision: UpcomingDecisionItem; onOpenCase: (caseId: number) => void }) {
  return (
    <li className="upcoming-decision-row">
      <button className="upcoming-decision-case" onClick={() => onOpenCase(decision.caseId)}>{decision.caseName}</button>
      <p className="upcoming-decision-type">{decision.decisionType}</p>
      {decision.context && <p className="helper-text">{decision.context}</p>}
      <div className="action-queue-item-meta">
        {decision.relevantDate && <span>By {decision.relevantDate}</span>}
        {decision.recommendedPreparationDate && <span>Prepare by {decision.recommendedPreparationDate}</span>}
        <span className="pill pill-neutral">{decision.status}</span>
      </div>
    </li>
  )
}
