import type { KeyboardEvent } from 'react'
import { useEffect, useRef } from 'react'

// Canonical confirmation dialog (design-system/MASTER.md \8.9: "ConfirmAction - inline two-step
// confirm (rows) or dialog (bulk/destructive); replaces the 27 native browser confirm() calls").
// This is the dialog half - a small centered modal reusing CommandPalette's overlay visual
// language. App owns a single instance via the confirmAction() promise helper in App.tsx; callers
// `await confirmAction({...})` instead of the native confirm(), so existing control flow (early
// returns on cancel) is preserved as `if (!(await confirmAction(...))) return`.

export type ConfirmOptions = {
  title: string
  message: string
  confirmLabel: string
  cancelLabel?: string
  danger?: boolean
}

export function ConfirmDialog({
  options,
  onConfirm,
  onCancel,
}: {
  options: ConfirmOptions
  onConfirm: () => void
  onCancel: () => void
}) {
  const cancelRef = useRef<HTMLButtonElement | null>(null)
  const previouslyFocused = useRef<HTMLElement | null>(null)

  // Focus the Cancel button by default (the safer action) and restore focus to whatever invoked
  // the dialog when it unmounts, matching CommandPalette's focus-return behavior.
  useEffect(() => {
    previouslyFocused.current = document.activeElement as HTMLElement | null
    cancelRef.current?.focus()
    return () => {
      previouslyFocused.current?.focus?.()
    }
  }, [])

  function handleKeyDown(event: KeyboardEvent<HTMLElement>) {
    if (event.key === 'Escape') {
      event.preventDefault()
      event.stopPropagation()
      onCancel()
    } else if (event.key === 'Enter') {
      const active = document.activeElement
      // Let a focused button's own click/Enter handling fire naturally rather than double-firing.
      if (active instanceof HTMLButtonElement) return
      event.preventDefault()
      onCancel()
    }
  }

  return (
    <div className="ui-command-overlay" role="presentation" onClick={onCancel}>
      <section
        className="ui-command-palette ui-confirm-dialog"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="confirm-dialog-title"
        aria-describedby="confirm-dialog-message"
        onClick={(event) => event.stopPropagation()}
        onKeyDown={handleKeyDown}
      >
        <h2 id="confirm-dialog-title" className="ui-confirm-title">{options.title}</h2>
        <p id="confirm-dialog-message" className="ui-confirm-message">{options.message}</p>
        <div className="ui-confirm-actions">
          <button
            ref={cancelRef}
            type="button"
            className="ui-btn ui-btn-secondary"
            onClick={onCancel}
          >
            {options.cancelLabel ?? 'Cancel'}
          </button>
          <button
            type="button"
            className={options.danger ? 'ui-btn ui-btn-danger' : 'ui-btn ui-btn-primary'}
            onClick={onConfirm}
          >
            {options.confirmLabel}
          </button>
        </div>
      </section>
    </div>
  )
}
