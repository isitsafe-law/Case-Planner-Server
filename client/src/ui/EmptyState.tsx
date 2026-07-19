import type { ReactNode } from 'react'

// Canonical empty-state primitive for the redesigned ui-* components. This is intentionally
// separate from src/dashboard/EmptyState.tsx (which stays as-is for the existing dashboard code).
export function EmptyState({
  title,
  hint,
  action,
  colSpan,
}: {
  title: string
  hint?: string
  action?: ReactNode
  colSpan?: number
}) {
  const body = (
    <div className="ui-empty" role="status">
      <p className="ui-empty-title">{title}</p>
      {hint && <p className="ui-empty-hint">{hint}</p>}
      {action && <div className="ui-empty-action">{action}</div>}
    </div>
  )

  if (colSpan != null) {
    return (
      <tr className="ui-empty-row">
        <td colSpan={colSpan}>{body}</td>
      </tr>
    )
  }

  return body
}
