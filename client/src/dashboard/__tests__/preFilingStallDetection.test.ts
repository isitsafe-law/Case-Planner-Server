import { describe, expect, it } from 'vitest'
import { computePreFilingStallInfo } from '../preFilingStallDetection'
import type { PreFilingMilestoneRecord, ReviewNoteRecord } from '../types'

function milestone(overrides: Partial<PreFilingMilestoneRecord> = {}): PreFilingMilestoneRecord {
  return { id: 1, caseId: 1, milestone: 'PleadingsPackageSent', isMarked: true, ...overrides }
}

function reviewNote(overrides: Partial<ReviewNoteRecord> = {}): ReviewNoteRecord {
  return { id: 1, caseId: 1, decision: 'Looks good', occurredDate: '2026-07-01', ...overrides }
}

const NOW = new Date('2026-07-28T12:00:00Z')

describe('computePreFilingStallInfo', () => {
  it('reports "No milestones marked yet" with a null age when nothing is marked and no review notes exist', () => {
    const info = computePreFilingStallInfo(1, [], [], NOW)
    expect(info).toEqual({ label: 'No milestones marked yet', daysStalled: null, isReturnedForRevision: false, nextMilestone: 'PleadingsPackageSent' })
  })

  it('labels by the NEXT milestone in sequence, not the furthest already marked', () => {
    const milestones = [
      milestone({ milestone: 'PleadingsPackageSent', isMarked: true, markedAt: '2026-07-20T00:00:00Z' }),
    ]
    const info = computePreFilingStallInfo(1, milestones, [], NOW)
    expect(info.label).toBe('Awaiting Chief Counsel Signatures Received')
    expect(info.daysStalled).toBe(8)
    expect(info.isReturnedForRevision).toBe(false)
    expect(info.nextMilestone).toBe('ChiefCounselSignaturesReceived')
  })

  it('reports completion once the last milestone in sequence is marked, with a null nextMilestone', () => {
    const milestones = [
      milestone({ milestone: 'ChiefCounselSignaturesReceived', isMarked: true, markedAt: '2026-07-27T00:00:00Z' }),
    ]
    const info = computePreFilingStallInfo(1, milestones, [], NOW)
    expect(info.label).toBe('All pre-filing milestones complete')
    expect(info.daysStalled).toBe(1)
    expect(info.nextMilestone).toBeNull()
  })

  it('uses the FURTHEST marked milestone, not the most recently marked one, when several are marked', () => {
    const milestones = [
      milestone({ id: 1, milestone: 'PleadingsPackageSent', isMarked: true, markedAt: '2026-07-01T00:00:00Z' }),
      milestone({ id: 2, milestone: 'ChiefCounselSignaturesReceived', isMarked: true, markedAt: '2026-07-15T00:00:00Z' }),
    ]
    const info = computePreFilingStallInfo(1, milestones, [], NOW)
    expect(info.label).toBe('All pre-filing milestones complete')
    expect(info.daysStalled).toBe(13)
  })

  it('switches the clock and label to a later "sent back for revision" review note', () => {
    const milestones = [
      milestone({ milestone: 'ChiefCounselSignaturesReceived', isMarked: true, markedAt: '2026-07-01T00:00:00Z' }),
    ]
    const notes = [
      reviewNote({ decision: 'Sent back for revision', createdAt: '2026-07-20T00:00:00Z' }),
    ]
    const info = computePreFilingStallInfo(1, milestones, notes, NOW)
    expect(info.label).toBe('Returned for revision, awaiting resubmission')
    expect(info.daysStalled).toBe(8)
    expect(info.isReturnedForRevision).toBe(true)
  })

  it('ignores a "sent back for revision" note OLDER than the last milestone mark', () => {
    const milestones = [
      milestone({ milestone: 'ChiefCounselSignaturesReceived', isMarked: true, markedAt: '2026-07-20T00:00:00Z' }),
    ]
    const notes = [
      reviewNote({ decision: 'Sent back for revision', createdAt: '2026-07-01T00:00:00Z' }),
    ]
    const info = computePreFilingStallInfo(1, milestones, notes, NOW)
    expect(info.label).toBe('All pre-filing milestones complete')
    expect(info.isReturnedForRevision).toBe(false)
  })

  it('ignores review notes with any decision other than "sent back for revision"', () => {
    const milestones = [
      milestone({ milestone: 'PleadingsPackageSent', isMarked: true, markedAt: '2026-07-01T00:00:00Z' }),
    ]
    const notes = [
      reviewNote({ decision: 'Looks good', createdAt: '2026-07-25T00:00:00Z' }),
    ]
    const info = computePreFilingStallInfo(1, milestones, notes, NOW)
    expect(info.isReturnedForRevision).toBe(false)
    expect(info.label).toBe('Awaiting Chief Counsel Signatures Received')
  })

  it('a "sent back for revision" note counts even with zero milestones marked yet', () => {
    const notes = [reviewNote({ decision: 'Sent back for revision', createdAt: '2026-07-10T00:00:00Z' })]
    const info = computePreFilingStallInfo(1, [], notes, NOW)
    expect(info.label).toBe('Returned for revision, awaiting resubmission')
    expect(info.isReturnedForRevision).toBe(true)
  })

  it('only considers rows/notes matching the given caseId', () => {
    const milestones = [
      milestone({ caseId: 1, milestone: 'ChiefCounselSignaturesReceived', isMarked: true, markedAt: '2026-07-27T00:00:00Z' }),
      milestone({ caseId: 2, milestone: 'PleadingsPackageSent', isMarked: true, markedAt: '2026-07-01T00:00:00Z' }),
    ]
    const info = computePreFilingStallInfo(1, milestones, [], NOW)
    expect(info.label).toBe('All pre-filing milestones complete')
  })

  it('uses the most recent "sent back for revision" note when more than one exists', () => {
    const notes = [
      reviewNote({ id: 1, decision: 'Sent back for revision', createdAt: '2026-07-05T00:00:00Z' }),
      reviewNote({ id: 2, decision: 'Sent back for revision', createdAt: '2026-07-20T00:00:00Z' }),
    ]
    const info = computePreFilingStallInfo(1, [], notes, NOW)
    expect(info.daysStalled).toBe(8)
  })
})
