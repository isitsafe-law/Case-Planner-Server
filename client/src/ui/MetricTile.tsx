export type MetricTileTone = 'danger' | 'warn' | 'default'

// Canonical metric-tile primitive (design-system §8.5): static display tile, or a toggleable
// filter facet when `onClick` is supplied. Replaces the old metric-tile / dashboard-summary-card /
// docket-metric trio with one component.
export function MetricTile({
  label,
  value,
  active,
  onClick,
  tone = 'default',
}: {
  label: string
  value: number | string
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
