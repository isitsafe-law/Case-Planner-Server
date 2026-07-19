import { formatDate } from './ui/format'

// Shared display for the manual-override pattern (original value / new value / reason /
// timestamp, no silent overwrite) - used by both deadline date history and activity-entry
// edit history so the two renderings never drift apart. Callers pre-format `previous`/`next`
// (they may be composite strings, not bare dates) via formatDate/displayDate; only `createdAt`
// is a raw timestamp this component formats itself.
export type EditHistoryRow = {
  id: number
  previous: string
  next: string
  reason?: string | null
  createdAt?: string | null
}

export function EditHistoryList({ rows, title }: { rows: EditHistoryRow[]; title?: string }) {
  if (rows.length === 0) return null
  return (
    <div className="top-gap-small">
      {title && <p className="helper-text">{title}</p>}
      <ul className="plain-list">
        {rows.map((row) => (
          <li key={row.id} className="flag-text muted">
            {row.previous || '—'} → {row.next || '—'}
            {row.reason ? ` — ${row.reason}` : ''}
            {row.createdAt ? ` (${formatDate(row.createdAt)})` : ''}
          </li>
        ))}
      </ul>
    </div>
  )
}
