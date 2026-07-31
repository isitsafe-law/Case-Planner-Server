export type DataQualityIssue = {
  key: string
  severity: string
  label: string
  count: number
  definition: string
  suggestedAction: string
  sampleCaseIds: number[]
}

export type DataQualityReport = {
  generatedAt: string
  scopeDefinition: string
  issues: DataQualityIssue[]
}

export const METRIC_DEFINITIONS = [
  ['Open cases', 'Cases whose consolidated status is not Triage or Resolved / Closed and whose legacy status is not Closed or Complete.'],
  ['Open tracts', 'The same open-case definition applied to tract-level case records; Triage is excluded from management totals.'],
  ['Needs attention', 'Open cases whose attention status is not onTrack or whose default-posture warning is active.'],
  ['Stalled pipeline', 'Pipeline cases remaining in the same pre-filing milestone state beyond the configured aging threshold.'],
  ['Next hard date', 'The earliest open deadline, future jury trial, or scheduled non-Other proceeding for the selected case group.'],
  ['Planned Work', 'Priority 4 attorney action-queue items that are appropriate to plan or advance but are not currently urgent, decision-required, discovery-blocked, or stale.'],
  ['Data-quality issue', 'A record-level condition that can make assignment, reporting, document generation, or workflow interpretation unreliable.'],
  ['Attorney workload signals', 'Transparent observational counts: open tracts, pipeline tracts, events in the next 30 days, overdue deadlines, and needs-attention cases. These are not yet a weighted workload score.'],
] as const
