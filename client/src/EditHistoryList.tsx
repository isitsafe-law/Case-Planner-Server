// Shared display for the manual-override pattern (original value / new value / reason /
// timestamp, no silent overwrite) - used by both deadline date history and activity-entry
// edit history so the two renderings never drift apart.
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
            {row.previous || 'Not set'} → {row.next || 'Not set'}
            {row.reason ? ` — ${row.reason}` : ''}
            {row.createdAt ? ` (${row.createdAt.slice(0, 10)})` : ''}
          </li>
        ))}
      </ul>
    </div>
  )
}
