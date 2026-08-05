import type { UpcomingScheduleItem } from './DashboardUpcomingSchedule'
import { DashboardUpcomingSchedule } from './DashboardUpcomingSchedule'

export type PlanningSummary = {
  juryTrials: number
  events: number
  deadlines: number
  nextJuryTrial?: { date: string; endDate?: string | null; caseName: string; caseId: number; eventId?: number | null; daysRemaining: number } | null
}

export function DashboardCompactSummaries({ planning, onJuryTrial, schedule, onEvent, onViewCalendar }: {
  planning: PlanningSummary
  onJuryTrial: () => void
  schedule: UpcomingScheduleItem[]
  onEvent: (item: UpcomingScheduleItem) => void
  onViewCalendar: () => void
}) {
  const trial = planning.nextJuryTrial
  return (
    <DashboardUpcomingSchedule
      items={schedule}
      onEvent={onEvent}
      onViewCalendar={onViewCalendar}
      featured={
        <button
          type="button"
          className="dashboard-schedule-item dashboard-schedule-item-featured"
          onClick={onJuryTrial}
          aria-label={trial ? `Next jury trial: ${trial.caseName}, ${trial.date}` : 'No upcoming jury trial'}
        >
          <span className="dashboard-schedule-date">
            {trial ? trial.date : 'None scheduled'}
            <small>{trial ? (trial.daysRemaining === 0 ? 'Today' : `${trial.daysRemaining} days`) : `${planning.juryTrials} in 180 days`}</small>
          </span>
          <span className="dashboard-schedule-detail">
            <strong>Next Jury Trial</strong>
            <span>{trial ? trial.caseName : 'None currently scheduled'}</span>
          </span>
        </button>
      }
    />
  )
}
