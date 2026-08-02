import { useMemo, useState } from 'react'
import type { CaseRecord, Hearing } from '../App'
import { upcomingJuryTrials } from './upcomingTrials'
import { Btn } from '../ui/Btn'
import { formatDate } from '../ui/format'
import { ReportExportActions } from './ReportExportActions'

type AttorneyAssignment = { caseId: number; name: string; role: string }

export function UpcomingTrialsReport({
  cases,
  hearings,
  assignments,
  attorneys,
  onOpenCase,
}: {
  cases: CaseRecord[]
  hearings: Hearing[]
  assignments: Record<number, AttorneyAssignment[]>
  attorneys: string[]
  onOpenCase: (caseId: number) => void
}) {
  const [horizon, setHorizon] = useState<'30' | '60' | '90' | '120' | '180' | 'all'>('180')
  const [attorney, setAttorney] = useState('')
  const [division, setDivision] = useState('')
  const today = new Date().toISOString().slice(0, 10)
  const divisionOptions = useMemo(() => [...new Set(cases.map((record) => record.division).filter((value): value is string => Boolean(value)))].sort(), [cases])
  const rows = useMemo(() => {
    const result = upcomingJuryTrials(cases, hearings, today, horizon === 'all' ? null : Number(horizon))
    return result.filter(({ caseRecord }) => {
      if (division && caseRecord.division !== division) return false
      if (!attorney) return true
      const names = new Set([caseRecord.assignedAttorney || '', ...(assignments[caseRecord.id] || []).map((item) => item.name)])
      return names.has(attorney)
    })
  }, [assignments, attorney, cases, division, hearings, horizon, today])
  const attorneyOptions = [...new Set([...attorneys, ...cases.map((record) => record.assignedAttorney || ''), ...Object.values(assignments).flat().map((item) => item.name)].filter(Boolean))].sort()

  const exportRows = rows.map(({ event, caseRecord }) => {
    const additional = [...new Set((assignments[caseRecord.id] || []).filter((item) => item.name && item.name !== caseRecord.assignedAttorney).map((item) => item.name))]
    const start = event.hearingDate || ''
    const end = event.endDate && event.endDate !== event.hearingDate ? event.endDate : ''
    const days = Math.max(0, Math.round((Date.parse(`${start}T00:00:00Z`) - Date.parse(`${today}T00:00:00Z`)) / 86400000))
    return { trialDate: end ? `${start} – ${end}` : start, case: caseRecord.caseName || caseRecord.caseNumber || `Case ${caseRecord.id}`, jobTract: [caseRecord.jobNumber, caseRecord.tract].filter(Boolean).join(' / '), county: caseRecord.county || '', primaryAttorney: caseRecord.assignedAttorney || 'Unassigned', additionalAttorneys: additional.join(', '), days }
  })

  return <section className="ui-table-panel">
    <div className="panel-hd"><h3>Upcoming Trials</h3><div className="ui-title-actions"><span className="pill pill-neutral">{rows.length} jury trial{rows.length === 1 ? '' : 's'}</span><ReportExportActions title="Upcoming Trials" filters={{ horizon: horizon === 'all' ? 'all upcoming' : `next ${horizon} days`, attorney: attorney || 'all', division: division || 'all' }} columns={[{ key: 'trialDate', label: 'Trial date' }, { key: 'case', label: 'Case' }, { key: 'jobTract', label: 'Job / tract' }, { key: 'county', label: 'County' }, { key: 'primaryAttorney', label: 'Primary attorney' }, { key: 'additionalAttorneys', label: 'Additional attorneys' }, { key: 'days', label: 'Days' }]} rows={exportRows} /></div></div>
    <div className="rep-fields">
      <label><span>Horizon</span><select value={horizon} onChange={(event) => setHorizon(event.target.value as typeof horizon)}><option value="30">Next 30 days</option><option value="60">Next 60 days</option><option value="90">Next 90 days</option><option value="120">Next 120 days</option><option value="180">Next 180 days</option><option value="all">All upcoming</option></select></label>
      <label><span>Attorney</span><select value={attorney} onChange={(event) => setAttorney(event.target.value)}><option value="">All attorneys</option>{attorneyOptions.map((name) => <option key={name}>{name}</option>)}</select></label>
      <label><span>Division</span><select value={division} onChange={(event) => setDivision(event.target.value)}><option value="">All divisions</option>{divisionOptions.map((value) => <option key={value}>{value}</option>)}</select></label>
    </div>
    <p className="helper-text top-gap-small">Source: active Jury Trial events. Legacy trial dates are not used to create report rows.</p>
    <div className="table-wrap top-gap-small">
      <table className="ui-table">
        <thead><tr><th>Trial date</th><th>Case</th><th>Job / tract</th><th>County</th><th>Primary attorney</th><th>Additional attorneys</th><th>Days</th><th></th></tr></thead>
        <tbody>{rows.length === 0 ? <tr><td colSpan={8}><p className="helper-text">No upcoming Jury Trial events match these filters.</p></td></tr> : rows.map(({ event, caseRecord }) => {
          const additional = [...new Set((assignments[caseRecord.id] || []).filter((item) => item.name && item.name !== caseRecord.assignedAttorney).map((item) => item.name))]
          const start = event.hearingDate || ''
          const end = event.endDate && event.endDate !== event.hearingDate ? event.endDate : null
          const days = Math.max(0, Math.round((Date.parse(`${start}T00:00:00Z`) - Date.parse(`${today}T00:00:00Z`)) / 86400000))
          return <tr key={event.id}><td className="ui-data">{formatDate(start)}{end ? ` – ${formatDate(end)}` : ''}</td><td>{caseRecord.caseName || caseRecord.caseNumber || `Case ${caseRecord.id}`}</td><td className="ui-data">{[caseRecord.jobNumber, caseRecord.tract].filter(Boolean).join(' / ') || '—'}</td><td>{caseRecord.county || '—'}</td><td>{caseRecord.assignedAttorney || 'Unassigned'}</td><td>{additional.join(', ') || '—'}</td><td className="ui-data">{days}</td><td><Btn size="sm" onClick={() => onOpenCase(caseRecord.id)}>Open case</Btn></td></tr>
        })}</tbody>
      </table>
    </div>
  </section>
}
