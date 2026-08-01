import { useEffect, useMemo, useState } from 'react'
import type { CaseRecord, Hearing } from '../App'
import { CASE_EVENT_TYPES } from '../eventTypes'
import { Btn } from '../ui/Btn'
import { formatDate } from '../ui/format'

export type CalendarEventPage = { total: number; limit: number; offset: number; items: Hearing[] }
export type CalendarEventQuery = { from?: string; to?: string; eventType?: string; assignedAttorney?: string; limit: number; offset: number }

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
  fetchEvents,
  currentUserName,
  onOpenCase,
  initialRange = 90,
}: {
  allCases: CaseRecord[]
  fetchEvents: (query: CalendarEventQuery) => Promise<CalendarEventPage>
  currentUserName?: string | null
  onOpenCase: (caseId: number) => void
  initialRange?: CalendarRange
}) {
  const [range, setRange] = useState<CalendarRange>(initialRange)
  const [scope, setScope] = useState(currentUserName ? 'My Events' : 'All Attorneys')
  const [eventType, setEventType] = useState('All')
  const [eventsPage, setEventsPage] = useState<CalendarEventPage>({ total: 0, limit: 100, offset: 0, items: [] })
  const [loading, setLoading] = useState(false)
  const [loadError, setLoadError] = useState('')
  const [page, setPage] = useState(0)
  useEffect(() => { setRange(initialRange); setPage(0) }, [initialRange])
  const caseById = useMemo(() => new Map(allCases.map((record) => [record.id, record])), [allCases])
  const attorneys = useMemo(() => Array.from(new Set(allCases.map((record) => record.assignedAttorney).filter(Boolean) as string[])).sort(), [allCases])
  const availableScopes = currentUserName ? ['My Events', 'All Attorneys', ...attorneys] : ['All Attorneys', ...attorneys]
  useEffect(() => {
    if (currentUserName && scope === 'All Attorneys') setScope('My Events')
  }, [currentUserName])
  const start = today()
  const end = range === 'all' ? null : start + range

  const isoDate = (day: number) => new Date(day * 86400000).toISOString().slice(0, 10)
  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setLoadError('')
    void fetchEvents({
      from: isoDate(start),
      ...(end == null ? {} : { to: isoDate(end) }),
      ...(eventType === 'All' ? {} : { eventType }),
      ...(scope === 'My Events' ? { assignedAttorney: currentUserName || undefined } : scope === 'All Attorneys' ? {} : { assignedAttorney: scope }),
      limit: 100,
      offset: page * 100,
    }).then((result) => {
      if (!cancelled) setEventsPage(result)
    }).catch((error) => {
      if (!cancelled) {
        setEventsPage({ total: 0, limit: 100, offset: page * 100, items: [] })
        setLoadError(error instanceof Error ? error.message : 'Unable to load calendar events.')
      }
    }).finally(() => {
      if (!cancelled) setLoading(false)
    })
    return () => { cancelled = true }
  }, [fetchEvents, start, end, eventType, scope, currentUserName, page])

  const events = useMemo(() => eventsPage.items.flatMap((event) => {
    const record = caseById.get(event.caseId)
    const eventStart = epochDay(event.hearingDate)
    const eventEnd = epochDay(event.endDate || event.hearingDate)
    if (!record || eventStart == null || eventEnd == null || eventEnd < start || (end != null && eventStart > end)) return []
    if ((scope === 'My Events' && record.assignedAttorney !== currentUserName) || (scope !== 'My Events' && scope !== 'All Attorneys' && record.assignedAttorney !== scope)) return []
    if (eventType !== 'All' && event.eventType !== eventType) return []
    return [{ event, record }]
  }).sort((a, b) => (a.event.hearingDate || '').localeCompare(b.event.hearingDate || '') || a.event.id - b.event.id), [eventsPage.items, caseById, start, end, scope, currentUserName, eventType])

  const updateFilter = (setter: (value: never) => void, value: never) => { setPage(0); setter(value) }
  const totalPages = Math.max(1, Math.ceil(eventsPage.total / 100))

  return (
    <main className="page">
      <div className="page-title-row">
        <div>
          <h2>Calendar</h2>
          <p className="helper-text">Case events only. Outlook availability is not included.</p>
        </div>
      </div>
      <div className="filter-bar top-gap-small">
        <label><span>Scope</span><select value={scope} onChange={(event) => updateFilter(setScope as (value: never) => void, event.target.value as never)}>{availableScopes.map((item) => <option key={item}>{item}</option>)}</select></label>
        <label><span>Range</span><select value={String(range)} onChange={(event) => updateFilter(setRange as (value: never) => void, (event.target.value === 'all' ? 'all' : Number(event.target.value)) as never)}>{GLOBAL_CALENDAR_RANGES.map((item) => <option key={item} value={item}>{item === 'all' ? 'See All' : `${item} days`}</option>)}</select></label>
        <label><span>Event type</span><select value={eventType} onChange={(event) => updateFilter(setEventType as (value: never) => void, event.target.value as never)}><option>All</option>{CASE_EVENT_TYPES.map((item) => <option key={item}>{item}</option>)}</select></label>
        <span className="filter-summary">{loading ? 'Loading events…' : `${eventsPage.total} upcoming event${eventsPage.total === 1 ? '' : 's'}`}</span>
      </div>
      <section className="ui-table-panel top-gap-small">
        {loadError && <p className="error-text top-gap-small">{loadError}</p>}
        <div className="table-wrap"><table className="ui-table compact-table"><thead><tr><th>Date</th><th>Event</th><th>Case</th><th>Job / Tract</th><th>Attorney</th><th>Location</th><th /></tr></thead><tbody>
          {events.length === 0 ? <tr><td colSpan={7}>{loading ? 'Loading calendar events…' : 'No upcoming events match the current scope and filters.'}</td></tr> : events.map(({ event, record }) => (
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
        {eventsPage.total > 100 && <div className="button-row compact-actions top-gap-small"><button onClick={() => setPage((current) => Math.max(0, current - 1))} disabled={page === 0 || loading}>Previous</button><span className="helper-text">Page {page + 1} of {totalPages}</span><button onClick={() => setPage((current) => Math.min(totalPages - 1, current + 1))} disabled={page + 1 >= totalPages || loading}>Next</button></div>}
      </section>
    </main>
  )
}
