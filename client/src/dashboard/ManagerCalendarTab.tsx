import { Fragment, useEffect, useMemo, useState } from 'react'
import type { CaseRecord, Hearing } from '../App'
import { formatDate } from '../ui/format'
import { EmptyState } from '../ui/EmptyState'
import { Btn } from '../ui/Btn'
import { downloadCsv } from '../ui/csvExport'
import { CASE_EVENT_TYPES } from '../eventTypes'

export const CALENDAR_HORIZONS = [7, 30, 60, 90, 120, 180, 'all'] as const
export type CalendarHorizon = typeof CALENDAR_HORIZONS[number]

// The authoritative event type for scheduled jury trials.
export const JURY_TRIAL_EVENT_TYPE = 'Jury Trial'
const INACTIVE_EVENT_STATUSES = new Set(['Canceled', 'Cancelled', 'Complete', 'Completed'])

type CalendarEvent = {
  key: string
  caseId: number
  date: string
  endDate?: string | null
  eventType: string
  title: string
  jobNumber: string
  tract: string
  assignedAttorney: string
  caseStatus: string
}

// Date-only ("YYYY-MM-DD") arithmetic via UTC epoch days - matches App.tsx's DateOnlyFromString
// convention (avoids local-timezone off-by-one drift on date-only strings).
function toEpochDay(value?: string | null): number | null {
  if (!value) return null
  const match = value.slice(0, 10).match(/^(\d{4})-(\d{2})-(\d{2})$/)
  if (!match) return null
  return Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])) / 86400000
}

function todayEpochDay(): number {
  const now = new Date()
  return Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()) / 86400000
}

// Monday-start week key for grouping ("Week of <Monday's date>").
function mondayWeekKey(dateStr: string): string {
  const day = toEpochDay(dateStr)
  if (day == null) return dateStr
  const dow = new Date(day * 86400000).getUTCDay() // 0 = Sunday .. 6 = Saturday
  const mondayDay = day - (dow === 0 ? 6 : dow - 1)
  return new Date(mondayDay * 86400000).toISOString().slice(0, 10)
}

// Counts used by the top-strip "Events next N days" tiles. Calendar and reports use the same
// hearing rows, including Jury Trial events, so counts do not drift to a legacy case date.
export function countEventsInWindow(allCases: CaseRecord[], hearings: Hearing[], days: number): number {
  const today = todayEpochDay()
  const windowEnd = today + days
  void allCases
  return hearings.filter((h) => {
    if (INACTIVE_EVENT_STATUSES.has(h.status || '')) return false
    const start = toEpochDay(h.hearingDate)
    const eventEnd = toEpochDay(h.endDate || h.hearingDate)
    return start != null && eventEnd != null && eventEnd >= today && start <= windowEnd
  }).length
}

export function ManagerCalendarTab({
  allCases,
  hearings,
  horizon,
  onHorizonChange,
  onOpenCase,
  initialEventType = 'All',
  initialAttorney = 'All',
  minimumDays = 0,
}: {
  allCases: CaseRecord[]
  hearings: Hearing[]
  horizon: CalendarHorizon
  onHorizonChange: (horizon: CalendarHorizon) => void
  onOpenCase: (caseId: number) => void
  initialEventType?: string
  initialAttorney?: string
  minimumDays?: number
}) {
  const [eventTypeFilter, setEventTypeFilter] = useState(initialEventType)
  const [attorneyFilter, setAttorneyFilter] = useState(initialAttorney)
  useEffect(() => { setEventTypeFilter(initialEventType); setAttorneyFilter(initialAttorney) }, [initialEventType, initialAttorney])

  const caseById = useMemo(() => new Map(allCases.map((c) => [c.id, c])), [allCases])
  const today = todayEpochDay()
  const windowStart = today + minimumDays
  const windowEnd = horizon === 'all' ? null : today + horizon
  const isActiveCase = (record?: CaseRecord) => Boolean(record) && (record!.caseStatus || 'Pipeline') !== 'Resolved / Closed' && record!.status !== 'Closed' && record!.status !== 'Complete'

  // Forward-looking events inside the selected horizon: authoritative hearings joined against
  // allCases for display columns.
  const windowEvents = useMemo(() => {
    const events: CalendarEvent[] = []
    for (const hearing of hearings) {
      const day = toEpochDay(hearing.hearingDate)
      const endDay = toEpochDay(hearing.endDate || hearing.hearingDate)
      if (day == null || endDay == null || INACTIVE_EVENT_STATUSES.has(hearing.status || '') || day < windowStart || endDay < today || (windowEnd != null && day > windowEnd)) continue
      const record = caseById.get(hearing.caseId)
      if (!isActiveCase(record)) continue
      events.push({
        key: `hearing-${hearing.id}`,
        caseId: hearing.caseId,
        date: hearing.hearingDate as string,
        endDate: hearing.endDate,
        eventType: hearing.eventType || 'Hearing',
        title: hearing.title,
        jobNumber: record?.jobNumber || '',
        tract: record?.tract || '',
        assignedAttorney: record?.assignedAttorney || '',
        caseStatus: record?.caseStatus || 'Pipeline',
      })
    }
    return events.sort((a, b) => a.date.localeCompare(b.date))
  }, [hearings, allCases, caseById, today, windowStart, windowEnd])

  // Past-due events are date-derived. A multi-day event remains current until its end date.
  const pastDueEvents = useMemo(() => {
    const events: CalendarEvent[] = []
    for (const hearing of hearings) {
      const day = toEpochDay(hearing.hearingDate)
      const endDay = toEpochDay(hearing.endDate || hearing.hearingDate)
      if (day == null || endDay == null || INACTIVE_EVENT_STATUSES.has(hearing.status || '') || endDay >= today) continue
      const record = caseById.get(hearing.caseId)
      if (!isActiveCase(record)) continue
      events.push({
        key: `pastdue-${hearing.id}`,
        caseId: hearing.caseId,
        date: hearing.hearingDate as string,
        endDate: hearing.endDate,
        eventType: hearing.eventType || 'Hearing',
        title: hearing.title,
        jobNumber: record?.jobNumber || '',
        tract: record?.tract || '',
        assignedAttorney: record?.assignedAttorney || '',
        caseStatus: record?.caseStatus || 'Pipeline',
      })
    }
    return events.sort((a, b) => a.date.localeCompare(b.date))
  }, [hearings, caseById, today])

  // Imminent: anything (hearing or trial) within the next 3 days - always a subset of windowEvents
  // since the smallest horizon is 7 days. Pulled out of the week-grouped table into the same
  // above-the-fold section as past-due items, so nothing urgent is buried mid-table.
  const imminentKeys = useMemo(() => {
    const keys = new Set<string>()
    for (const event of windowEvents) {
      const day = toEpochDay(event.date)
      if (day != null && day - today <= 3) keys.add(event.key)
    }
    return keys
  }, [windowEvents, today])

  const forwardEvents = windowEvents.filter((event) => !imminentKeys.has(event.key))
  const imminentEvents = windowEvents.filter((event) => imminentKeys.has(event.key))
  const pinnedEvents = useMemo(
    () => [...pastDueEvents, ...imminentEvents].sort((a, b) => a.date.localeCompare(b.date)),
    [pastDueEvents, imminentEvents],
  )
  const pastDueKeySet = useMemo(() => new Set(pastDueEvents.map((e) => e.key)), [pastDueEvents])

  const eventTypeOptions = useMemo(() => {
    const set = new Set<string>()
    windowEvents.forEach((event) => set.add(event.eventType))
    CASE_EVENT_TYPES.forEach((type) => set.add(type))
    return Array.from(set).sort()
  }, [windowEvents])

  const attorneyOptions = useMemo(() => {
    const set = new Set<string>()
    windowEvents.forEach((event) => { if (event.assignedAttorney) set.add(event.assignedAttorney) })
    return Array.from(set).sort()
  }, [windowEvents])

  const matchesFilters = (event: CalendarEvent) =>
    (eventTypeFilter === 'All' || event.eventType === eventTypeFilter) &&
    (attorneyFilter === 'All' || event.assignedAttorney === attorneyFilter)

  const visiblePinned = pinnedEvents.filter(matchesFilters)
  const visibleForward = forwardEvents.filter(matchesFilters)

  const groupedForward = useMemo(() => {
    const groups = new Map<string, CalendarEvent[]>()
    for (const event of visibleForward) {
      const key = mondayWeekKey(event.date)
      if (!groups.has(key)) groups.set(key, [])
      groups.get(key)!.push(event)
    }
    return Array.from(groups.entries()).sort((a, b) => a[0].localeCompare(b[0]))
  }, [visibleForward])

  const totalVisible = visiblePinned.length + visibleForward.length

  function exportCsv() {
    const rows = [...visiblePinned, ...visibleForward].map((event) => ({
      Date: formatDate(event.date) + (event.endDate ? ` - ${formatDate(event.endDate)}` : ''),
      'Event Type': event.eventType,
      'Job Number': event.jobNumber,
      Tract: event.tract,
      Attorney: event.assignedAttorney,
      'Case Status': event.caseStatus,
      Flag: pastDueKeySet.has(event.key) ? 'Past due' : imminentKeys.has(event.key) ? 'Imminent' : '',
    }))
    downloadCsv(`Division_Calendar_${horizon === 'all' ? 'all' : `${horizon}day`}_${new Date().toISOString().slice(0, 10)}.csv`, rows)
  }

  function renderRow(event: CalendarEvent) {
    const stripe = pastDueKeySet.has(event.key) ? 'p1' : imminentKeys.has(event.key) ? 'p2' : undefined
    return (
      <tr key={event.key}>
        <td className={stripe ? `ui-stripe-cell ${stripe}` : undefined}>
          {formatDate(event.date)}{event.endDate ? ` – ${formatDate(event.endDate)}` : ''}
        </td>
        <td>{event.eventType}</td>
        <td>{[event.jobNumber, event.tract].filter(Boolean).join(' · ') || '—'}</td>
        <td>{event.assignedAttorney || '—'}</td>
        <td>{event.caseStatus}</td>
        <td><Btn size="sm" onClick={() => onOpenCase(event.caseId)}>Open Case</Btn></td>
      </tr>
    )
  }

  return (
    <div>
      <div className="button-row compact-actions top-gap-small" style={{ marginBottom: '0.75rem' }}>
        <div className="segmented-tabs compact-segments" style={{ maxWidth: 260 }}>
          {CALENDAR_HORIZONS.map((h) => (
            <button key={h} className={h === horizon ? 'segment active' : 'segment'} onClick={() => onHorizonChange(h)}>
              {h === 'all' ? 'See All' : `${h} days`}
            </button>
          ))}
        </div>
        <label><span>Event type</span>
          <select value={eventTypeFilter} onChange={(event) => setEventTypeFilter(event.target.value)}>
            <option value="All">All event types</option>
            {eventTypeOptions.map((type) => <option key={type} value={type}>{type}</option>)}
          </select>
        </label>
        <label><span>Attorney</span>
          <select value={attorneyFilter} onChange={(event) => setAttorneyFilter(event.target.value)}>
            <option value="All">All attorneys</option>
            {attorneyOptions.map((name) => <option key={name} value={name}>{name}</option>)}
          </select>
        </label>
        <Btn onClick={exportCsv} disabled={totalVisible === 0}>Export CSV</Btn>
      </div>

      {totalVisible === 0 ? (
        <EmptyState title={horizon === 'all' ? 'No future events for active cases.' : `No events in the next ${horizon} days.`} />
      ) : (
        <div className="table-wrap">
          <table className="ui-table compact-table">
            <thead>
              <tr>
                <th>Date</th>
                <th>Event Type</th>
                <th>Job + Tract</th>
                <th>Attorney</th>
                <th>Case Status</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {visiblePinned.length > 0 && (
                <>
                  <tr className="ui-week-row"><td colSpan={6}>Needs attention now</td></tr>
                  {visiblePinned.map((event) => renderRow(event))}
                </>
              )}
              {groupedForward.map(([weekKey, events]) => (
                <Fragment key={weekKey}>
                  <tr className="ui-week-row"><td colSpan={6}>Week of {formatDate(weekKey)}</td></tr>
                  {events.map((event) => renderRow(event))}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
