// Shared "why this appears / short posture" block reused by ActionQueueItemCard and
// FilingPipelineRow - the dashboard brief's progressive-disclosure rule: a short posture summary
// on the dashboard, full detail on the case page.
export function MatterPostureSummary({
  reason,
  posture,
  nextAction,
}: {
  reason: string
  posture: string
  nextAction: string
}) {
  return (
    <div className="matter-posture-summary">
      <p className="matter-posture-reason">{reason}</p>
      <p className="matter-posture-text">{posture}</p>
      <p className="matter-posture-next"><strong>Next:</strong> {nextAction}</p>
    </div>
  )
}
