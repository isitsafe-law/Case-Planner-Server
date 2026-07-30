import { useEffect, useMemo, useState } from 'react'
import type { CaseRecord, Hearing } from '../App'
import { CASE_EVENT_TYPES } from '../eventTypes'
import { Btn } from '../ui/Btn'
import { formatDate } from '../ui/format'

export const GLOBAL_CALENDAR_RANGES = [30, 60, 90, 120, 180, 'all'] as const
type CalendarRange = typeof GLOBAL_CALENDAR_RANGES[number]

function epochDay(value?: string | null): number | null {
  if (!value) return null
  const match = value.slice(0, 10).match(/^(\d{4})-(\d{2})-(\d{2})$/)
  return match ? Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])) / 86400000 : null
}

function today(): number {
  const now = new Date()
  return Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()) / 86400000
}

function dateRange(event: Hearing): string {
  const start = event.hearingDate ? formatDate(event.hearingDate) : 'Date not set'
  return event.endDate && event.endDate !== event.hearingDate ? `${start} – ${formatDate(event.endDate)}` : start
}

export function GlobalCalendarTab({
  allCases,
  hearings,
  currentUserName,
  onOpenCase,
}: {
  allCases: CaseRecord[]
  hearings: Hearing[]
  currentUserName?: string | null
  onOpenCase: (caseId: number) => void
}) {
  const [range, setRange] = useState<CalendarRange>(90)
  const [scope, setScope] = useState(currentUserName ? 'My Events' : 'All Attorneys')
  const [eventType, setEventType] = useState('All')
  const caseById = useMemo(() => new Map(allCases.map((record) => [record.id, record])), [allCases])
  const attorneys = useMemo(() => Array.from(new Set(allCases.map((record) => record.assignedAttorney).filter(Boolean) as string[])).sort(), [allCases])
  const availableScopes = currentUserName ? ['My Events', 'All Attorneys', ...attorneys] : ['All Attorneys', ...attorneys]
  useEffect(() => {
    if (currentUserName && scope === 'All Attorneys') setScope('My Events')
  }, [currentUserName])
  const start = today()
  const end = range === 'all' ? null : start + range

  const events = useMemo(() => hearings.flatMap((event) => {
    const record = caseById.get(event.caseId)
    const eventStart = epochDay(event.hearingDate)
    const eventEnd = epochDay(event.endDate || event.hearingDate)
    if (!record || eventStart == null || eventEnd == null || eventEnd < start || (end != null && eventStart > end)) return []
    if ((scope === 'My Events' && record.assignedAttorney !== currentUserName) || (scope !== 'My Events' && scope !== 'All Attorneys' && record.assignedAttorney !== scope)) return []
    if (eventType !== 'All' && event.eventType !== eventType) return []
    return [{ event, record }]
  }).sort((a, b) => (a.event.hearingDate || '').localeCompare(b.event.hearingDate || '') || a.event.id - b.event.id), [hearings, caseById, start, end, scope, currentUserName, eventType])

  return (
    <main className="page">
      <div className="page-title-row">
        <div>
          <h2>Calendar</h2>
          <p className="helper-text">Case events only. Outlook availability is not included.</p>
        </div>
      </div>
      <div className="filter-bar top-gap-small">
        <label><span>Scope</span><select value={scope} onChange={(event) => setScope(event.target.value)}>{availableScopes.map((item) => <option key={item}>{item}</option>)}</select></label>
        <label><span>Range</span><select value={String(range)} onChange={(event) => setRange(event.target.value === 'all' ? 'all' : Number(event.target.value) as CalendarRange)}>{GLOBAL_CALENDAR_RANGES.map((item) => <option key={item} value={item}>{item === 'all' ? 'See All' : `${item} days`}</option>)}</select></label>
        <label><span>Event type</span><select value={eventType} onChange={(event) => setEventType(event.target.value)}><option>All</option>{CASE_EVENT_TYPES.map((item) => <option key={item}>{item}</option>)}</select></label>
        <span className="filter-summary">{events.length} upcoming event{events.length === 1 ? '' : 's'}</span>
      </div>
      <section className="ui-table-panel top-gap-small">
        <div className="table-wrap"><table className="ui-table compact-table"><thead><tr><th>Date</th><th>Event</th><th>Case</th><th>Job / Tract</th><th>Attorney</th><th>Location</th><th /></tr></thead><tbody>
          {events.length === 0 ? <tr><td colSpan={7}>No upcoming events match the current scope and filters.</td></tr> : events.map(({ event, record }) => (
            <tr key={event.id}>
              <td className="ui-data">{dateRange(event)}{event.startTime ? ` · ${event.startTime}` : ''}</td>
              <td><strong>{event.eventType || 'Other'}</strong><div className="ui-sub">{event.title}</div>{event.description && <div className="ui-sub">{event.description}</div>}</td>
              <td>{record.caseName || record.caseNumber || `Case ${record.id}`}</td>
              <td className="ui-data">{[record.jobNumber, record.tract].filter(Boolean).join(' · ') || '—'}</td>
              <td>{record.assignedAttorney || 'Unassigned'}</td>
              <td>{event.location || '—'}</td>
              <td><Btn size="sm" onClick={() => onOpenCase(record.id)}>Open Case</Btn></td>
            </tr>
          ))}
        </tbody></table></div>
      </section>
    </main>
  )
}
