import type { KeyboardEvent, ReactNode } from 'react'
import { useEffect, useMemo, useRef, useState } from 'react'

// Canonical command palette + shortcut-help overlay (design-system/MASTER.md §8 #13: "CommandPalette
// - Ctrl+K: actions, navigation, case search; plus shortcut overlay (?)"). Fully props-driven: App
// supplies the grouped content (Navigation / Actions / Cases) and the query hook that feeds the
// debounced case search; this component owns only the shell, filtering, highlighting, and keyboard
// navigation. Escape is handled locally (via the input's onKeyDown, which stops propagation) rather
// than a global window listener, so closing the palette never also closes a Drawer/modal it happens
// to be layered over - only the topmost overlay reacts to Escape.

export type CommandItem = {
  id: string
  label: string
  hint?: string
  shortcut?: string
  action: () => void
}

export type CommandGroup = {
  label: string
  items: CommandItem[]
}

type FlatOption = {
  item: CommandItem
}

const MAX_PER_GROUP = 10

function highlightLabel(label: string, query: string): ReactNode {
  const trimmed = query.trim()
  if (!trimmed) return label
  const lower = label.toLowerCase()
  const needle = trimmed.toLowerCase()
  const index = lower.indexOf(needle)
  if (index === -1) return label
  return (
    <>
      {label.slice(0, index)}
      <mark>{label.slice(index, index + needle.length)}</mark>
      {label.slice(index + needle.length)}
    </>
  )
}

export function CommandPalette({
  open,
  onClose,
  groups,
  onQuery,
}: {
  open: boolean
  onClose: () => void
  groups: CommandGroup[]
  onQuery?: (query: string) => void
}) {
  const [query, setQuery] = useState('')
  const [activeId, setActiveId] = useState<string | null>(null)
  const inputRef = useRef<HTMLInputElement | null>(null)
  const previouslyFocused = useRef<HTMLElement | null>(null)

  // Reset the query and move focus into the input every time the palette opens; restore focus to
  // whatever invoked it (app-bar chip, or wherever the cursor was) when it closes.
  useEffect(() => {
    if (!open) return
    previouslyFocused.current = document.activeElement as HTMLElement | null
    setQuery('')
    onQuery?.('')
    inputRef.current?.focus()
    return () => {
      previouslyFocused.current?.focus?.()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  const filteredGroups = useMemo(() => {
    const needle = query.trim().toLowerCase()
    return groups
      .map((group) => ({
        label: group.label,
        items: (needle ? group.items.filter((item) => item.label.toLowerCase().includes(needle)) : group.items).slice(0, MAX_PER_GROUP),
      }))
      .filter((group) => group.items.length > 0)
  }, [groups, query])

  const flatOptions = useMemo<FlatOption[]>(() => {
    const flat: FlatOption[] = []
    for (const group of filteredGroups) {
      for (const item of group.items) flat.push({ item })
    }
    return flat
  }, [filteredGroups])

  useEffect(() => {
    if (flatOptions.length === 0) {
      setActiveId(null)
      return
    }
    if (!flatOptions.some((option) => option.item.id === activeId)) {
      setActiveId(flatOptions[0].item.id)
    }
  }, [flatOptions, activeId])

  if (!open) return null

  function runItem(item: CommandItem) {
    item.action()
    onClose()
  }

  function moveActive(delta: number) {
    if (flatOptions.length === 0) return
    const currentIndex = flatOptions.findIndex((option) => option.item.id === activeId)
    const nextIndex = (currentIndex + delta + flatOptions.length) % flatOptions.length
    setActiveId(flatOptions[nextIndex].item.id)
  }

  function handleInputChange(value: string) {
    setQuery(value)
    onQuery?.(value)
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      moveActive(1)
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      moveActive(-1)
    } else if (event.key === 'Enter') {
      event.preventDefault()
      const active = flatOptions.find((option) => option.item.id === activeId)
      if (active) runItem(active.item)
    } else if (event.key === 'Escape') {
      event.preventDefault()
      // Stop this from reaching any window-level Escape listener (e.g. Drawer's) underneath -
      // the palette is the topmost layer, so only it should close.
      event.stopPropagation()
      onClose()
    }
  }

  const listboxId = 'command-palette-listbox'

  return (
    <div className="ui-command-overlay" role="presentation" onClick={onClose}>
      <section
        className="ui-command-palette"
        role="dialog"
        aria-modal="true"
        aria-label="Command palette"
        onClick={(event) => event.stopPropagation()}
      >
        <input
          ref={inputRef}
          className="ui-command-input"
          type="text"
          value={query}
          onChange={(event) => handleInputChange(event.target.value)}
          onKeyDown={handleKeyDown}
          role="combobox"
          aria-expanded="true"
          aria-controls={listboxId}
          aria-activedescendant={activeId ? `cp-option-${activeId}` : undefined}
          aria-autocomplete="list"
          aria-label="Command palette search"
          placeholder="Search cases or actions…"
          autoComplete="off"
          spellCheck={false}
        />
        <div id={listboxId} role="listbox" aria-label="Command palette results" className="ui-command-list">
          {flatOptions.length === 0 ? (
            <div className="ui-command-empty">No matches — try a case name or an action</div>
          ) : (
            filteredGroups.map((group) => (
              <div className="ui-command-group" key={group.label} role="group" aria-label={group.label}>
                <div className="ui-command-group-label">{group.label}</div>
                {group.items.map((item) => {
                  const active = item.id === activeId
                  return (
                    <div
                      key={item.id}
                      id={`cp-option-${item.id}`}
                      role="option"
                      aria-selected={active}
                      className={active ? 'ui-command-option active' : 'ui-command-option'}
                      onMouseEnter={() => setActiveId(item.id)}
                      onMouseDown={(event) => event.preventDefault()}
                      onClick={() => runItem(item)}
                    >
                      <span className="ui-command-option-label">{highlightLabel(item.label, query)}</span>
                      {item.hint && <span className="ui-command-option-hint">{item.hint}</span>}
                      {item.shortcut && <span className="ui-command-option-shortcut">{item.shortcut}</span>}
                    </div>
                  )
                })}
              </div>
            ))
          )}
        </div>
      </section>
    </div>
  )
}

// Static "?" help overlay listing the app's keyboard shortcuts. Reuses the palette's shell/overlay
// styling but has no input - it's a plain, focus-trapped dialog that Escape (or a scrim click)
// closes.
export function ShortcutHelpDialog({ onClose }: { onClose: () => void }) {
  const panelRef = useRef<HTMLElement | null>(null)
  const previouslyFocused = useRef<HTMLElement | null>(null)

  useEffect(() => {
    previouslyFocused.current = document.activeElement as HTMLElement | null
    panelRef.current?.focus()
    return () => {
      previouslyFocused.current?.focus?.()
    }
  }, [])

  function handleKeyDown(event: KeyboardEvent<HTMLElement>) {
    if (event.key === 'Escape') {
      event.preventDefault()
      event.stopPropagation()
      onClose()
    }
  }

  return (
    <div className="ui-command-overlay" role="presentation" onClick={onClose}>
      <section
        ref={panelRef}
        className="ui-command-palette ui-shortcut-dialog"
        role="dialog"
        aria-modal="true"
        aria-label="Keyboard shortcuts"
        tabIndex={-1}
        onClick={(event) => event.stopPropagation()}
        onKeyDown={handleKeyDown}
      >
        <div className="ui-command-group-label">Keyboard shortcuts</div>
        <ul className="ui-shortcut-list">
          <li><kbd>Ctrl</kbd> + <kbd>K</kbd><span>Open command palette</span></li>
          <li><kbd>?</kbd><span>Show this help</span></li>
          <li><kbd>Esc</kbd><span>Close dialogs and drawers</span></li>
          <li><kbd>↑</kbd> / <kbd>↓</kbd><span>Move selection in the command palette</span></li>
          <li><kbd>Enter</kbd><span>Run the selected command</span></li>
        </ul>
      </section>
    </div>
  )
}
