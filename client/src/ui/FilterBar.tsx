import type { ReactNode } from 'react'

export function FilterBar({ children }: { children: ReactNode }) {
  return <div className="ui-filterbar">{children}</div>
}

export function FilterChip({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: ReactNode
}) {
  return (
    <button
      type="button"
      className={active ? 'ui-chip ui-chip-on' : 'ui-chip'}
      aria-pressed={active}
      onClick={onClick}
    >
      {children}
    </button>
  )
}

export function FilterSep() {
  return <span className="ui-filter-sep" aria-hidden="true" />
}

export function FilterSummary({ children }: { children: ReactNode }) {
  return <span className="ui-filter-summary">{children}</span>
}
