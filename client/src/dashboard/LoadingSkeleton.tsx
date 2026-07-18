export function LoadingSkeleton({ rows = 3 }: { rows?: number }) {
  return (
    <div className="dashboard-skeleton" aria-hidden="true">
      {Array.from({ length: rows }).map((_, i) => (
        <div key={i} className="dashboard-skeleton-row" />
      ))}
    </div>
  )
}
