import type { ReactNode } from 'react'

export type MetricTileTone = 'danger' | 'warn' | 'default'

// Canonical metric-tile primitive (design-system §8.5): static display tile, or a toggleable
// filter facet when `onClick` is supplied. Replaces the old metric-tile / dashboard-summary-card /
// docket-metric trio with one component. `value` accepts a ReactNode so callers can append a
// muted unit suffix (e.g. "411 <span>days</span>") without a second component variant.
export function MetricTile({
  label,
  value,
  active,
  onClick,
  tone = 'default',
}: {
  label: string
  value: ReactNode
  active?: boolean
  onClick?: () => void
  tone?: MetricTileTone
}) {
  const classes = ['ui-tile', tone !== 'default' ? `ui-tile-${tone}` : '', active ? 'ui-tile-on' : '']
    .filter(Boolean)
    .join(' ')

  if (onClick) {
    return (
      <button type="button" className={classes} onClick={onClick} aria-pressed={!!active}>
        <span className="ui-tile-label">{label}</span>
        <span className="ui-tile-value">{value}</span>
      </button>
    )
  }

  return (
    <div className={classes}>
      <span className="ui-tile-label">{label}</span>
      <span className="ui-tile-value">{value}</span>
    </div>
  )
}
