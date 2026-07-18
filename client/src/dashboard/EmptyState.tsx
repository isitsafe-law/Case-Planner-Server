export function EmptyState({ title, description }: { title: string; description: string }) {
  return (
    <div className="dashboard-empty-state" role="status">
      <p className="dashboard-empty-title">{title}</p>
      <p className="helper-text">{description}</p>
    </div>
  )
}
