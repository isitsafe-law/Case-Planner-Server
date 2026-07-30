import { CORE_PRE_FILING_MILESTONE_ORDER, preFilingMilestoneLabel, type PreFilingMilestone, type PreFilingMilestoneRecord, type ReviewNoteRecord } from './types'
import { isReturnedForRevisionDecision } from '../case-workspace/ReviewNotesLog'

// Final implementation, item 3: ONE aging calculation per tract, not two parallel ones - shared by
// NeedsAttentionTab.tsx (the division-wide exception feed) and IncomingPipelinePanel.tsx (the
// Division Overview's per-tract pipeline view), so both surfaces agree on what's stalled and why,
// distinguished only by which label/age they render, never by separately re-deriving it.
export type PreFilingStallInfo = {
  label: string
  daysStalled: number | null
  isReturnedForRevision: boolean
  // The milestone a "Mark" action here would target - null once every milestone is complete. A
  // review note never changes which milestone is next; only the aging clock/label switches when
  // one exists (see item 1c: IncomingPipelinePanel's inline mark action reads this directly rather
  // than re-deriving "what's next" itself).
  nextMilestone: PreFilingMilestone | null
}

// Whole days elapsed from an ISO timestamp to `now` (default: actual now) - same floor-not-round,
// never-negative convention as NeedsAttentionTab.tsx's daysSince, generalized here to a different
// basis (milestone markedAt / review note createdAt).
function daysSince(dateStr: string, now: Date): number {
  const diffMs = now.getTime() - new Date(dateStr).getTime()
  return Math.max(0, Math.floor(diffMs / 86_400_000))
}

// Base case: time since the most recent milestone was marked, labeled by what's next in sequence -
// NOT the furthest-marked milestone itself (that's IncomingPipelinePanel's OLD framing, retired in
// favor of this shared one). If a later review note with decision "sent back for revision" exists,
// the clock and label switch to measure from THAT note instead - "most recent" is compared using
// each event's own system-entry timestamp (markedAt / createdAt), not the user-entered, potentially
// backdated occurredDate, so a backdated entry can never make an already-known-about event look
// artificially newer.
export function computePreFilingStallInfo(
  caseId: number,
  milestones: PreFilingMilestoneRecord[],
  reviewNotes: ReviewNoteRecord[],
  now: Date = new Date(),
): PreFilingStallInfo {
  const caseMilestones = milestones.filter((m) => m.caseId === caseId)
  const caseReviewNotes = reviewNotes.filter((n) => n.caseId === caseId)

  let furthestIndex = -1
  let furthestMarkedAt: string | null = null
  for (const record of caseMilestones) {
    if (!record.isMarked) continue
    const index = CORE_PRE_FILING_MILESTONE_ORDER.indexOf(record.milestone as (typeof CORE_PRE_FILING_MILESTONE_ORDER)[number])
    if (index > furthestIndex) {
      furthestIndex = index
      furthestMarkedAt = record.markedAt || null
    }
  }

  const returnNotes = caseReviewNotes
    .filter((note) => isReturnedForRevisionDecision(note.decision) && note.createdAt)
    .sort((a, b) => (b.createdAt || '').localeCompare(a.createdAt || ''))
  const mostRecentReturnNote = returnNotes[0]

  const nextMilestone: PreFilingMilestone | null = furthestIndex === -1
    ? CORE_PRE_FILING_MILESTONE_ORDER[0]
    : (CORE_PRE_FILING_MILESTONE_ORDER[furthestIndex + 1] ?? null)

  if (mostRecentReturnNote && (!furthestMarkedAt || (mostRecentReturnNote.createdAt || '') > furthestMarkedAt)) {
    return {
      label: 'Returned for revision, awaiting resubmission',
      daysStalled: mostRecentReturnNote.createdAt ? daysSince(mostRecentReturnNote.createdAt, now) : null,
      isReturnedForRevision: true,
      nextMilestone,
    }
  }

  if (furthestIndex === -1) {
    return { label: 'No milestones marked yet', daysStalled: null, isReturnedForRevision: false, nextMilestone }
  }

  return {
    label: nextMilestone ? `Awaiting ${preFilingMilestoneLabel(nextMilestone)}` : 'All pre-filing milestones complete',
    daysStalled: furthestMarkedAt ? daysSince(furthestMarkedAt, now) : null,
    isReturnedForRevision: false,
    nextMilestone,
  }
}
