import type { ReactNode } from 'react'

export type StatusTone = 'ok' | 'warn' | 'danger' | 'neutral' | 'primary'

export function StatusChip({ tone, children }: { tone: StatusTone; children: ReactNode }) {
  return (
    <span className={`ui-status ui-status-${tone}`}>
      <i aria-hidden="true" />
      <span>{children}</span>
    </span>
  )
}
