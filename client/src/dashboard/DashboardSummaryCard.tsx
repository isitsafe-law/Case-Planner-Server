export function DashboardSummaryCard({
  label,
  value,
  active,
  onClick,
}: {
  label: string
  value: number
  active: boolean
  onClick: () => void
}) {
  return (
    <button
      type="button"
      className={`dashboard-summary-card${active ? ' active' : ''}`}
      onClick={onClick}
      aria-pressed={active}
    >
      <span className="dashboard-summary-card-label">{active ? '✓ ' : ''}{label}</span>
      <strong className="dashboard-summary-card-value">{value}</strong>
    </button>
  )
}
