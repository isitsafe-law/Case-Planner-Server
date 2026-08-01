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
  return <div className="dashboard-planning-column">
    <button type="button" className="dashboard-next-trial-card" onClick={onJuryTrial} aria-label={trial ? `Next jury trial: ${trial.caseName}, ${trial.date}` : 'No upcoming jury trial'}>
      <span>Next jury trial</span>
      <strong>{trial ? trial.date : 'None scheduled'}</strong>
      <small>{trial ? trial.caseName : `${planning.juryTrials} within 180 days`}</small>
      {trial && <em>{trial.daysRemaining === 0 ? 'Today' : `${trial.daysRemaining} days remaining`}</em>}
    </button>
    <DashboardUpcomingSchedule items={schedule} onEvent={onEvent} onViewCalendar={onViewCalendar} />
  </div>
}
