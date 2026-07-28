import type { StatusCount } from './dashboardAggregation'

// Compact segmented/stacked-bar variant of this app's established .bars pattern (index.css, "Open-
// case age distribution: direct-labeled horizontal bars, no legend" - consumed by the Reports tab's
// Open-case age chart). That original is a vertical list of single-color bars, one per band/row;
// this is the same visual language (a proportional bar, tabular-nums count labels, theme-aware
// tokens, no reliance on a color-only legend) reshaped into a single horizontal stacked bar so it
// fits one table cell per attorney/job row. Each status gets its own count label directly above its
// own segment (sharing the same flex proportions as the bar itself) rather than a separate legend -
// per the spec's "labeled with its count, not just a color legend" requirement - so the color
// coding is a bonus visual grouping, not the only way to read a segment's value.
const STATUS_SEGMENT_CLASS: Record<string, string> = {
  'Pipeline': 'seg-pipeline',
  'Filed / Service Pending': 'seg-filed',
  'Active Litigation': 'seg-active',
  'Settlement Pending': 'seg-settlement',
  'Trial Preparation': 'seg-trial',
  'Resolved / Closed': 'seg-resolved',
}

export function StatusDistributionBar({ counts }: { counts: StatusCount[] }) {
  const total = counts.reduce((sum, c) => sum + c.count, 0)
  const ariaLabel = `Tract counts by status: ${counts.map((c) => `${c.status}: ${c.count}`).join(', ')}`

  if (total === 0) {
    return <span className="subtle-text">No tracts</span>
  }

  return (
    <div className="status-stack" role="img" aria-label={ariaLabel}>
      <div className="status-stack-counts">
        {counts.map((c) => (
          <span key={c.status} style={{ flex: c.count > 0 ? c.count : 0.0001 }}>
            {c.count > 0 ? c.count : ''}
          </span>
        ))}
      </div>
      <div className="status-stack-bar">
        {counts.map((c) => (
          <span
            key={c.status}
            className={STATUS_SEGMENT_CLASS[c.status]}
            style={{ flex: c.count > 0 ? c.count : 0.0001 }}
            title={`${c.status}: ${c.count}`}
          />
        ))}
      </div>
    </div>
  )
}

// Shared one-line color key, rendered once above a table that uses StatusDistributionBar - a
// supplement to (not a replacement for) each segment's own count label, for anyone relying on the
// color grouping to scan multiple rows at a glance.
export function StatusDistributionLegend() {
  return (
    <p className="helper-text status-stack-legend">
      {(['Pipeline', 'Filed / Service Pending', 'Active Litigation', 'Settlement Pending', 'Trial Preparation', 'Resolved / Closed'] as const).map((status) => (
        <span key={status} className="status-stack-legend-item">
          <i className={STATUS_SEGMENT_CLASS[status]} />
          {status}
        </span>
      ))}
    </p>
  )
}
