import { MetricTile } from '../ui/MetricTile'
import type { CaseRecord, DeadlineItem, Hearing } from '../App'
import type { PreFilingMilestoneAgingSummary } from './types'

export type ManagerSummaryBar = { key: string; label: string; count: number; detail: string }

function todayEpoch(): number {
  const now = new Date()
  return Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()) / 86400000
}
function epoch(value?: string | null): number | null {
  const match = value?.slice(0, 10).match(/^(\d{4})-(\d{2})-(\d{2})$/)
  return match ? Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])) / 86400000 : null
}

function activeCase(record?: CaseRecord | null): boolean {
  return Boolean(record) && (record!.caseStatus || 'Pipeline') !== 'Resolved / Closed' && record!.status !== 'Closed' && record!.status !== 'Complete' && record!.status !== 'Triage'
}

function eventIsHard(event: Hearing): boolean {
  return event.eventType !== 'Other' && event.eventType !== 'Meeting' && event.eventType !== 'Inspection'
}

function eventIsActive(event: Hearing): boolean {
  return !['Canceled', 'Cancelled', 'Complete', 'Completed'].includes(event.status || '')
}

export function buildManagerHardDateBars(allCases: CaseRecord[], hearings: Hearing[], deadlines: DeadlineItem[]): ManagerSummaryBar[] {
  const cases = new Map(allCases.map((record) => [record.id, record]))
  const start = todayEpoch()
  const buckets = [
    { key: '0-30', label: '0–30 days', max: 30, count: 0, events: 0, deadlines: 0 },
    { key: '31-60', label: '31–60 days', max: 60, count: 0, events: 0, deadlines: 0 },
    { key: '61-90', label: '61–90 days', max: 90, count: 0, events: 0, deadlines: 0 },
    { key: '91-120', label: '91–120 days', max: 120, count: 0, events: 0, deadlines: 0 },
    { key: '121-180', label: '121–180 days', max: 180, count: 0, events: 0, deadlines: 0 },
  ]
  const add = (date: string | null | undefined, kind: 'events' | 'deadlines') => {
    const day = epoch(date)
    if (day == null) return
    const days = day - start
    const bucket = buckets.find((candidate, index) => days >= (index === 0 ? 0 : buckets[index - 1].max + 1) && days <= candidate.max)
    if (!bucket) return
    bucket.count += 1
    bucket[kind] += 1
  }
  hearings.filter(eventIsHard).forEach((event) => { if (activeCase(cases.get(event.caseId))) add(event.hearingDate, 'events') })
  deadlines.forEach((deadline) => { if (activeCase(cases.get(deadline.caseId)) && deadline.status !== 'Done' && deadline.status !== 'Complete') add(deadline.dueDate, 'deadlines') })
  return buckets.map((bucket) => ({ key: bucket.key, label: bucket.label, count: bucket.count, detail: `${bucket.events} events · ${bucket.deadlines} deadlines` }))
}

export function buildManagerTrialBars(allCases: CaseRecord[], hearings: Hearing[]): ManagerSummaryBar[] {
  const cases = new Map(allCases.map((record) => [record.id, record]))
  const today = todayEpoch()
  const counts = new Map<string, number>()
  const seen = new Set<number>()
  for (const hearing of hearings.filter((item) => item.eventType === 'Jury Trial' && eventIsActive(item))) {
    const record = cases.get(hearing.caseId)
    const day = epoch(hearing.hearingDate)
    if (!record || day == null || day < today || day > today + 180 || !activeCase(record) || seen.has(record.id)) continue
    seen.add(record.id)
    const attorney = record.assignedAttorney || 'Unassigned'
    counts.set(attorney, (counts.get(attorney) || 0) + 1)
  }
  return Array.from(counts.entries()).sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0])).map(([attorney, count]) => ({ key: attorney, label: attorney, count, detail: `${count} jury trial${count === 1 ? '' : 's'} within 180 days` }))
}

export function buildManagerPipelineBars(allCases: CaseRecord[], aging: PreFilingMilestoneAgingSummary | null): ManagerSummaryBar[] {
  const pipeline = allCases.filter((record) => activeCase(record) && (record.caseStatus || 'Pipeline') === 'Pipeline')
  const bucketCounts = new Map<string, number>()
  for (const record of pipeline) {
    const row = aging?.cases.find((item) => item.caseId === record.id)
    const stage = row?.furthestMilestone || 'None'
    bucketCounts.set(stage, (bucketCounts.get(stage) || 0) + 1)
  }
  return Array.from(bucketCounts.entries()).sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0])).map(([stage, count]) => ({ key: stage, label: stage === 'None' ? 'No milestone yet' : stage, count, detail: 'pipeline cases' }))
}

export function ManagerDashboardVisuals({ attentionCount, hardDateCount, trialCount, pipelineStallCount, serviceRiskCount, onAttention, onHardDates, onTrials, onPipeline, onService }: {
  attentionCount: number
  hardDateCount: number
  trialCount: number
  pipelineStallCount: number
  serviceRiskCount: number
  onAttention: () => void
  onHardDates: () => void
  onTrials: () => void
  onPipeline: () => void
  onService: () => void
}) {
  return <div className="ui-tiles dashboard-kpi-strip">
    <MetricTile label="Management Attention" value={attentionCount} hint="Exceptions needing review" tone={attentionCount > 0 ? 'danger' : 'default'} onClick={onAttention} />
    <MetricTile label="Hard Dates · 90 days" value={hardDateCount} onClick={onHardDates} />
    <MetricTile label="Jury Trials · 180 days" value={trialCount} onClick={onTrials} />
    <MetricTile label="Pipeline Stalls" value={pipelineStallCount} hint="Beyond review threshold" tone={pipelineStallCount > 0 ? 'warn' : 'default'} onClick={onPipeline} />
    <MetricTile label="Service Risk · 90+ days" value={serviceRiskCount} tone={serviceRiskCount > 0 ? 'warn' : 'default'} onClick={onService} />
  </div>
}
