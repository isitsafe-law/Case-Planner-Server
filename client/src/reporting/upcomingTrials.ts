export type UpcomingTrialCase = {
  id: number
  caseName?: string | null
  caseNumber?: string | null
  jobNumber?: string | null
  tract?: string | null
  county?: string | null
  division?: string | null
  caseStatus?: string | null
  status?: string | null
  assignedAttorney?: string | null
}

export type UpcomingTrialEvent = {
  id: number
  caseId: number
  eventType?: string | null
  hearingDate?: string | null
  endDate?: string | null
  status?: string | null
}

export type UpcomingTrialRow = {
  event: UpcomingTrialEvent
  caseRecord: UpcomingTrialCase
}

const JURY_TRIAL_EVENT_TYPE = 'Jury Trial'
const INACTIVE_EVENT_STATUSES = new Set(['Canceled', 'Cancelled', 'Complete', 'Completed'])

function dateOnly(value?: string | null): string | null {
  return value?.slice(0, 10).match(/^\d{4}-\d{2}-\d{2}$/) ? value!.slice(0, 10) : null
}

export function upcomingJuryTrials(
  cases: UpcomingTrialCase[],
  events: UpcomingTrialEvent[],
  today: string,
  horizonDays: number | null,
): UpcomingTrialRow[] {
  const endOfWindow = horizonDays == null ? null : addDays(today, horizonDays)
  const caseById = new Map(cases.map((record) => [record.id, record]))
  return events
    .filter((event) => event.eventType === JURY_TRIAL_EVENT_TYPE && !INACTIVE_EVENT_STATUSES.has(event.status || ''))
    .map((event) => ({ event, caseRecord: caseById.get(event.caseId) }))
    .filter((row): row is UpcomingTrialRow => {
      const record = row.caseRecord
      const start = dateOnly(row.event.hearingDate)
      const end = dateOnly(row.event.endDate || row.event.hearingDate)
      if (!record || !start || !end || end < today) return false
      if (record.caseStatus === 'Resolved / Closed' || record.caseStatus === 'Triage' || record.status === 'Closed' || record.status === 'Complete' || record.status === 'Triage') return false
      return start <= (endOfWindow || '9999-12-31')
    })
    .sort((left, right) => (dateOnly(left.event.hearingDate) || '').localeCompare(dateOnly(right.event.hearingDate) || '') || left.event.id - right.event.id)
}

function addDays(value: string, days: number): string {
  const date = new Date(`${value}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}
