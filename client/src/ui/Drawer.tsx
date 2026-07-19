import type { ReactNode } from 'react'
import { useEffect, useRef } from 'react'
import { Btn } from './Btn'

// Canonical right-side drawer primitive (design-system/MASTER.md \8.8: "Modal / Drawer / Popover
// - the only 3 overlay primitives"). Dirty-guard semantics live in the caller's `onClose` - this
// component only decides *when* a close is requested (scrim click, ✕ button, Escape), not
// whether the request is honored.
export function Drawer({
  title,
  width = 640,
  onClose,
  children,
  footer,
}: {
  title: string
  width?: number
  onClose: () => void
  children: ReactNode
  footer?: ReactNode
}) {
  const panelRef = useRef<HTMLElement | null>(null)
  const previouslyFocused = useRef<HTMLElement | null>(null)

  // Move focus into the drawer on open; return it to the invoking element on close/unmount.
  useEffect(() => {
    previouslyFocused.current = document.activeElement as HTMLElement | null
    panelRef.current?.focus()
    return () => {
      previouslyFocused.current?.focus?.()
    }
  }, [])

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  return (
    <div className="ui-drawer-overlay" role="presentation" onClick={onClose}>
      <section
        ref={panelRef}
        className="ui-drawer"
        style={{ width: `min(${width}px, 100vw)` }}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="ui-drawer-header">
          <h3>{title}</h3>
          <Btn variant="ghost" size="sm" className="ui-btn-icon" aria-label="Close" onClick={onClose}>✕</Btn>
        </div>
        <div className="ui-drawer-body">{children}</div>
        {footer && <div className="ui-drawer-footer">{footer}</div>}
      </section>
    </div>
  )
}
