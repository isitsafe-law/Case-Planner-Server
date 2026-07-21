import { useEffect, useRef } from 'react'
import { formatDateTime } from './format'

// Multi-user rollout Phase 4a (notifications core, design-system/MASTER.md §8 #14 "AppBar"). Fully
// props-driven like CommandPalette - App owns the fetch/poll/mark-read plumbing and the current
// notification list; this component only renders the bell, badge, and dropdown, and reports
// interactions back up. Reuses formatDateTime (the one canonical date-time formatter) rather than
// inventing a relative-time ("2h ago") formatter.

export type NotificationItem = {
  id: number
  caseId: number | null
  notificationType: string
  title: string
  body: string | null
  isRead: boolean
  createdAt: string
}

export function NotificationBell({
  items,
  unreadCount,
  open,
  onToggle,
  onClose,
  onSelect,
  onMarkAllRead,
}: {
  items: NotificationItem[]
  unreadCount: number
  open: boolean
  onToggle: () => void
  onClose: () => void
  onSelect: (item: NotificationItem) => void
  onMarkAllRead: () => void
}) {
  const containerRef = useRef<HTMLDivElement | null>(null)

  // Close on outside click or Escape - same convention as the search suggestions dropdown right
  // next to it in the app bar.
  useEffect(() => {
    if (!open) return
    function handlePointerDown(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) onClose()
    }
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') onClose()
    }
    document.addEventListener('mousedown', handlePointerDown)
    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('mousedown', handlePointerDown)
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [open, onClose])

  const badgeLabel = unreadCount > 99 ? '99+' : String(unreadCount)

  return (
    <div className="notification-bell" ref={containerRef}>
      <button
        type="button"
        className="kbd-hit notification-bell-trigger"
        aria-label={unreadCount > 0 ? `Notifications, ${unreadCount} unread` : 'Notifications'}
        aria-expanded={open}
        aria-haspopup="menu"
        onClick={onToggle}
      >
        <svg width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true">
          <path
            d="M8 1.5a3.5 3.5 0 0 0-3.5 3.5v1.6c0 .8-.28 1.58-.79 2.2L2.7 10.1c-.5.6-.08 1.5.7 1.5h9.2c.78 0 1.2-.9.7-1.5l-1.01-1.3a3.5 3.5 0 0 1-.79-2.2V5A3.5 3.5 0 0 0 8 1.5Z"
            stroke="currentColor"
            strokeWidth="1.2"
            strokeLinejoin="round"
          />
          <path d="M6.3 13.2a1.8 1.8 0 0 0 3.4 0" stroke="currentColor" strokeWidth="1.2" strokeLinecap="round" />
        </svg>
        {unreadCount > 0 && <span className="notification-badge">{badgeLabel}</span>}
      </button>
      {open && (
        <div className="notification-dropdown" role="menu" aria-label="Notifications">
          <div className="notification-dropdown-header">
            <span>Notifications</span>
            {unreadCount > 0 && (
              <button type="button" className="notification-mark-all" onClick={onMarkAllRead}>
                Mark all read
              </button>
            )}
          </div>
          {items.length === 0 ? (
            <div className="notification-empty">No notifications yet</div>
          ) : (
            <ul className="notification-list">
              {items.map((item) => (
                <li key={item.id}>
                  <button
                    type="button"
                    className={item.isRead ? 'notification-row' : 'notification-row unread'}
                    onClick={() => onSelect(item)}
                  >
                    <span className="notification-row-title">{item.title}</span>
                    {item.body && <span className="notification-row-body">{item.body}</span>}
                    <span className="notification-row-time">{formatDateTime(item.createdAt)}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  )
}
