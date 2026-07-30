import { useMemo, useState } from 'react'
import type { CaseRecord } from '../App'
import { Btn } from '../ui/Btn'
import { EmptyState } from './EmptyState'
import { downloadCsv } from '../ui/csvExport'
import type { PreFilingMilestoneRecord, ReviewNoteRecord } from './types'
import { computePreFilingStallInfo } from './preFilingStallDetection'

type SortKey = 'job' | 'tract' | 'holder' | 'stage' | 'nextAction' | 'followUp' | 'activity'

function daysSince(value?: string | null): number | null {
  if (!value) return null
  const date = new Date(value.slice(0, 10) + 'T00:00:00Z')
  if (Number.isNaN(date.getTime())) return null
  return Math.max(0, Math.floor((Date.now() - date.getTime()) / 86_400_000))
}

export function DivisionPipelineTab({
  allCases,
  preFilingMilestones,
  reviewNotes,
  onOpenCase,
}: {
  allCases: CaseRecord[]
  preFilingMilestones: PreFilingMilestoneRecord[]
  reviewNotes: ReviewNoteRecord[]
  onOpenCase: (caseId: number) => void
}) {
  const [search, setSearch] = useState('')
  const [holder, setHolder] = useState('All')
  const [stage, setStage] = useState('All')
  const [sortKey, setSortKey] = useState<SortKey>('job')
  const [descending, setDescending] = useState(false)

  const pipeline = useMemo(() => allCases.filter((record) => (record.caseStatus || 'Pipeline') === 'Pipeline'), [allCases])
  const holders = useMemo(() => Array.from(new Set(pipeline.map((record) => record.currentHolder || 'Unassigned'))).sort(), [pipeline])
  const stages = useMemo(() => Array.from(new Set(pipeline.map((record) => record.pipelineStage || record.stage || 'Stage not set'))).sort(), [pipeline])

  const rows = useMemo(() => {
    const query = search.trim().toLocaleLowerCase()
    const filtered = pipeline.filter((record) => {
      const stall = computePreFilingStallInfo(record.id, preFilingMilestones, reviewNotes)
      const haystack = [record.jobNumber, record.tract, record.caseName, record.landowner, record.assignedAttorney].filter(Boolean).join(' ').toLocaleLowerCase()
      return (!query || haystack.includes(query)) &&
        (holder === 'All' || (record.currentHolder || 'Unassigned') === holder) &&
        (stage === 'All' || (record.pipelineStage || record.stage || 'Stage not set') === stage) &&
        stall !== null
    })
    return filtered.sort((a, b) => {
      const value = (record: CaseRecord): string => {
        switch (sortKey) {
          case 'job': return record.jobNumber || ''
          case 'tract': return record.tract || ''
          case 'holder': return record.currentHolder || 'Unassigned'
          case 'stage': return record.pipelineStage || record.stage || ''
          case 'nextAction': return record.nextAction || 'zzzz'
          case 'followUp': return record.nextReviewDate || '9999-12-31'
          case 'activity': return record.lastMeaningfulActivityDate || record.dateSentToCurrentHolder || ''
        }
      }
      const comparison = value(a).localeCompare(value(b), undefined, { numeric: true })
      return (descending ? -1 : 1) * comparison
    })
  }, [pipeline, preFilingMilestones, reviewNotes, search, holder, stage, sortKey, descending])

  function changeSort(next: SortKey) {
    if (next === sortKey) setDescending((current) => !current)
    else { setSortKey(next); setDescending(false) }
  }

  function exportFiltered() {
    downloadCsv(`Division_Pipeline_${new Date().toISOString().slice(0, 10)}.csv`, rows.map((record) => ({
      'Job Number': record.jobNumber || '', Tract: record.tract || '', 'Case Name': record.caseName || '',
      Attorney: record.assignedAttorney || 'Unassigned', Holder: record.currentHolder || 'Unassigned',
      Stage: record.pipelineStage || record.stage || '', 'Next Action': record.nextAction || '',
      'Follow-up Date': record.nextReviewDate || '', 'Days Since Activity': daysSince(record.lastMeaningfulActivityDate || record.dateSentToCurrentHolder) ?? '',
    })))
  }

  return (
    <div className="division-pipeline">
      <div className="pipeline-toolbar">
        <label><span>Search pipeline</span><input type="search" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Job, tract, case, owner, or attorney" /></label>
        <label><span>Holder</span><select value={holder} onChange={(event) => setHolder(event.target.value)}><option>All</option>{holders.map((item) => <option key={item}>{item}</option>)}</select></label>
        <label><span>Stage</span><select value={stage} onChange={(event) => setStage(event.target.value)}><option>All</option>{stages.map((item) => <option key={item}>{item}</option>)}</select></label>
        <Btn onClick={exportFiltered} disabled={rows.length === 0}>Export CSV</Btn>
      </div>
      <div className="pipeline-results-summary">{rows.length} of {pipeline.length} pipeline matter{pipeline.length === 1 ? '' : 's'} shown</div>
      {rows.length === 0 ? <EmptyState title={pipeline.length === 0 ? 'No pre-filing matters right now.' : 'No pipeline matters match these filters.'} description="Adjust the filters or search terms to broaden the result." /> : (
        <div className="division-pipeline-list">
          {rows.map((record) => {
            const stall = computePreFilingStallInfo(record.id, preFilingMilestones, reviewNotes)
            const age = daysSince(record.lastMeaningfulActivityDate || record.dateSentToCurrentHolder)
            return <article className="division-pipeline-row" key={record.id}>
              <div className="pipeline-row-identity"><strong>{[record.jobNumber, record.tract].filter(Boolean).join(' · ') || 'Unnumbered matter'}</strong><span>{record.caseName || record.landowner || 'Unnamed case'}</span><span className="subtle-text">{record.assignedAttorney || 'Unassigned attorney'}</span></div>
              <div><span className="pipeline-field-label">Holder</span><strong>{record.currentHolder || 'Unassigned'}</strong></div>
              <div><span className="pipeline-field-label">Stage / status</span><strong>{record.pipelineStage || record.stage || 'Stage not set'}</strong><span>{stall?.label || 'No pre-filing milestone recorded'}</span></div>
              <div><span className="pipeline-field-label">Next action</span><strong>{record.nextAction || 'Not set'}</strong><span>{record.nextReviewDate ? `Follow-up ${record.nextReviewDate}` : 'No follow-up date'}</span></div>
              <div><span className="pipeline-field-label">Last activity</span><strong>{age == null ? 'No date' : `${age} day${age === 1 ? '' : 's'} ago`}</strong><span>{stall?.isReturnedForRevision ? 'Returned for revision' : 'Active pipeline matter'}</span></div>
              <div className="pipeline-row-actions"><Btn size="sm" onClick={() => onOpenCase(record.id)}>Open Case</Btn></div>
            </article>
          })}
        </div>
      )}
      {rows.length > 0 && <div className="pipeline-sort-controls"><span>Sort:</span>{(['job','tract','holder','stage','nextAction','followUp','activity'] as SortKey[]).map((key) => <button key={key} className={sortKey === key ? 'link-button active' : 'link-button'} onClick={() => changeSort(key)}>{key === 'job' ? 'Job' : key === 'tract' ? 'Tract' : key === 'nextAction' ? 'Next action' : key === 'followUp' ? 'Follow-up' : key === 'activity' ? 'Last activity' : key}{sortKey === key ? (descending ? ' ↓' : ' ↑') : ''}</button>)}</div>}
    </div>
  )
}
