import { MetricTile } from '../ui/MetricTile'
import type { CaseRecord, DeadlineItem, Hearing } from '../App'
import type { PreFilingMilestoneAgingSummary } from './types'

export type ManagerSummaryBar = {
  key: string
  label: string
  count: number
  detail: string
}

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
  for (const hearing of hearings) {
    if (eventIsHard(hearing) && activeCase(cases.get(hearing.caseId))) add(hearing.hearingDate, 'events')
  }
  for (const deadline of deadlines) {
    if (activeCase(cases.get(deadline.caseId)) && deadline.status !== 'Done' && deadline.status !== 'Complete') add(deadline.dueDate, 'deadlines')
  }
  return buckets.map((bucket) => ({ key: bucket.key, label: bucket.label, count: bucket.count, detail: `${bucket.events} events · ${bucket.deadlines} deadlines` }))
}

export function buildManagerTrialBars(allCases: CaseRecord[], hearings: Hearing[]): ManagerSummaryBar[] {
  const cases = new Map(allCases.map((record) => [record.id, record]))
  const today = todayEpoch()
  const counts = new Map<string, number>()
  const seen = new Set<number>()
  for (const hearing of hearings.filter((item) => item.eventType === 'Jury Trial')) {
    const record = cases.get(hearing.caseId)
    const day = epoch(hearing.hearingDate)
    if (!record || day == null || day < today || day > today + 180 || !activeCase(record) || seen.has(record.id)) continue
    seen.add(record.id)
    const attorney = record.assignedAttorney || 'Unassigned'
    counts.set(attorney, (counts.get(attorney) || 0) + 1)
  }
  // Legacy cases can still have only the controlling case-level trial date. Include them once.
  for (const record of allCases) {
    const day = epoch(record.trialDate)
    if (day == null || day < today || day > today + 180 || !activeCase(record) || seen.has(record.id)) continue
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

function SummaryBars({ title, bars, onSelect }: { title: string; bars: ManagerSummaryBar[]; onSelect: (bar: ManagerSummaryBar) => void }) {
  const max = Math.max(1, ...bars.map((bar) => bar.count))
  return <section className="dashboard-visual-panel" aria-labelledby={`manager-${title.replaceAll(' ', '-').toLowerCase()}`}>
    <div className="dashboard-panel-heading"><h3 id={`manager-${title.replaceAll(' ', '-').toLowerCase()}`}>{title}</h3><span className="helper-text">Division view</span></div>
    {bars.length === 0 || bars.every((bar) => bar.count === 0) ? <p className="dashboard-visual-empty">No records in this range.</p> : <div className="dashboard-bar-list">{bars.map((bar) => <button key={bar.key} type="button" className="dashboard-bar-row" onClick={() => onSelect(bar)} aria-label={`${bar.label}: ${bar.count}. ${bar.detail}`}><span className="dashboard-bar-label">{bar.label}</span><span className="dashboard-bar-track"><span className="dashboard-bar-fill" style={{ width: `${Math.max(6, (bar.count / max) * 100)}%` }} /></span><strong className="dashboard-bar-count">{bar.count}</strong><span className="dashboard-bar-detail">{bar.detail}</span></button>)}</div>}
  </section>
}

export function ManagerDashboardVisuals({ allCases, hearings, deadlines, aging, attentionCount, hardDateCount, trialCount, pipelineStallCount, serviceRiskCount, onAttention, onHardDates, onTrials, onPipeline, onService }: {
  allCases: CaseRecord[]
  hearings: Hearing[]
  deadlines: DeadlineItem[]
  aging: PreFilingMilestoneAgingSummary | null
  attentionCount: number
  hardDateCount: number
  trialCount: number
  pipelineStallCount: number
  serviceRiskCount: number
  onAttention: () => void
  onHardDates: (bar?: ManagerSummaryBar) => void
  onTrials: (bar?: ManagerSummaryBar) => void
  onPipeline: () => void
  onService: () => void
}) {
  const hardDates = buildManagerHardDateBars(allCases, hearings, deadlines)
  const trials = buildManagerTrialBars(allCases, hearings)
  const pipeline = buildManagerPipelineBars(allCases, aging)
  return <>
    <div className="ui-tiles dashboard-kpi-strip">
      <MetricTile label="Management Attention" value={attentionCount} tone={attentionCount > 0 ? 'danger' : 'default'} onClick={onAttention} />
      <MetricTile label="Hard Dates · 90 days" value={hardDateCount} onClick={() => onHardDates()} />
      <MetricTile label="Jury Trials · 180 days" value={trialCount} onClick={() => onTrials()} />
      <MetricTile label="Pipeline Stalls" value={pipelineStallCount} tone={pipelineStallCount > 0 ? 'warn' : 'default'} onClick={onPipeline} />
      <MetricTile label="Service Risk · 90+ days" value={serviceRiskCount} tone={serviceRiskCount > 0 ? 'warn' : 'default'} onClick={onService} />
    </div>
    <div className="dashboard-visual-grid manager-dashboard-visual-grid">
      <SummaryBars title="Upcoming Hard Dates" bars={hardDates} onSelect={onHardDates} />
      <SummaryBars title="Jury Trials by Attorney" bars={trials} onSelect={onTrials} />
      <SummaryBars title="Pipeline Aging by Stage" bars={pipeline} onSelect={onPipeline ? () => onPipeline() : () => undefined} />
    </div>
  </>
}
