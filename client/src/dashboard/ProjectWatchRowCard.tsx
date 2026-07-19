import type { ProjectWatchRow } from './types'
import { formatDate } from '../ui/format'

// Named ProjectWatchRowCard (not ProjectWatchRow) to avoid colliding with the ProjectWatchRow
// *type* in ./types - same component the dashboard brief calls "ProjectWatchRow".
export function ProjectWatchRowCard({ project }: { project: ProjectWatchRow }) {
  return (
    <div className="project-watch-row">
      <div className="project-watch-header">
        <strong>{project.projectName}</strong>
        {project.jobNumber && <span className="subtle-text"> · Job {project.jobNumber}</span>}
      </div>
      <div className="action-queue-item-meta">
        <span>{project.tractCount} tracts</span>
        <span>{project.preFilingCount} pre-filing</span>
        <span>{project.filedCount} filed</span>
        <span>{project.resolvedCount} resolved</span>
        <span>{project.onAttorneyDeskCount} on desk</span>
        {project.stalledCount > 0 && <span className="pill pill-warn">{project.stalledCount} stalled</span>}
        {project.earliestTrialDate && <span>Earliest trial {formatDate(project.earliestTrialDate)}</span>}
      </div>
      {project.sharedIssue && (
        <p className="helper-text top-gap-small"><strong>Shared issue:</strong> {project.sharedIssue}</p>
      )}
      {project.nextProjectDecision && (
        <p className="helper-text"><strong>Next:</strong> {project.nextProjectDecision}</p>
      )}
    </div>
  )
}
