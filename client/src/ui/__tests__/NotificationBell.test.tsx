import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { NotificationBell, type NotificationItem } from '../NotificationBell'

function items(overrides?: Partial<NotificationItem>[]): NotificationItem[] {
  const base: NotificationItem[] = [
    {
      id: 1,
      caseId: 42,
      notificationType: 'TaskAssigned',
      title: 'Task assigned',
      body: "Task 'Serve interrogatories' assigned to you on 24-CV-100.",
      isRead: false,
      createdAt: '2026-07-18T16:32:00Z',
    },
  ]
  if (!overrides) return base
  return overrides.map((o, i) => ({ ...base[0], id: i + 1, ...o }))
}

describe('NotificationBell', () => {
  it('shows no badge when unread count is zero', () => {
    render(
      <NotificationBell
        items={[]}
        unreadCount={0}
        open={false}
        onToggle={() => {}}
        onClose={() => {}}
        onSelect={() => {}}
        onMarkAllRead={() => {}}
      />,
    )
    expect(screen.getByRole('button', { name: 'Notifications' })).toBeInTheDocument()
    expect(screen.queryByText(/unread/)).not.toBeInTheDocument()
  })

  it('shows the unread count badge and includes it in the accessible name', () => {
    render(
      <NotificationBell
        items={items()}
        unreadCount={3}
        open={false}
        onToggle={() => {}}
        onClose={() => {}}
        onSelect={() => {}}
        onMarkAllRead={() => {}}
      />,
    )
    expect(screen.getByRole('button', { name: /3 unread/ })).toBeInTheDocument()
    expect(screen.getByText('3')).toBeInTheDocument()
  })

  it('caps the badge label at 99+', () => {
    render(
      <NotificationBell
        items={items()}
        unreadCount={140}
        open={false}
        onToggle={() => {}}
        onClose={() => {}}
        onSelect={() => {}}
        onMarkAllRead={() => {}}
      />,
    )
    expect(screen.getByText('99+')).toBeInTheDocument()
  })

  it('calls onToggle when the bell is clicked', async () => {
    const user = userEvent.setup()
    const onToggle = vi.fn()
    render(
      <NotificationBell
        items={[]}
        unreadCount={0}
        open={false}
        onToggle={onToggle}
        onClose={() => {}}
        onSelect={() => {}}
        onMarkAllRead={() => {}}
      />,
    )
    await user.click(screen.getByRole('button', { name: 'Notifications' }))
    expect(onToggle).toHaveBeenCalledTimes(1)
  })

  it('renders an empty state when open with no notifications', () => {
    render(
      <NotificationBell
        items={[]}
        unreadCount={0}
        open
        onToggle={() => {}}
        onClose={() => {}}
        onSelect={() => {}}
        onMarkAllRead={() => {}}
      />,
    )
    expect(screen.getByText('No notifications yet')).toBeInTheDocument()
  })

  it('renders notification rows with title and body, and calls onSelect when clicked', async () => {
    const user = userEvent.setup()
    const onSelect = vi.fn()
    const list = items()
    render(
      <NotificationBell
        items={list}
        unreadCount={1}
        open
        onToggle={() => {}}
        onClose={() => {}}
        onSelect={onSelect}
        onMarkAllRead={() => {}}
      />,
    )
    expect(screen.getByText('Task assigned')).toBeInTheDocument()
    expect(screen.getByText("Task 'Serve interrogatories' assigned to you on 24-CV-100.")).toBeInTheDocument()

    await user.click(screen.getByText('Task assigned'))
    expect(onSelect).toHaveBeenCalledWith(list[0])
  })

  it('shows a Mark all read action only when there is unread mail, and calls onMarkAllRead', async () => {
    const user = userEvent.setup()
    const onMarkAllRead = vi.fn()
    const { rerender } = render(
      <NotificationBell
        items={items()}
        unreadCount={0}
        open
        onToggle={() => {}}
        onClose={() => {}}
        onSelect={() => {}}
        onMarkAllRead={onMarkAllRead}
      />,
    )
    expect(screen.queryByText('Mark all read')).not.toBeInTheDocument()

    rerender(
      <NotificationBell
        items={items()}
        unreadCount={1}
        open
        onToggle={() => {}}
        onClose={() => {}}
        onSelect={() => {}}
        onMarkAllRead={onMarkAllRead}
      />,
    )
    await user.click(screen.getByText('Mark all read'))
    expect(onMarkAllRead).toHaveBeenCalledTimes(1)
  })

  it('calls onClose on Escape while open', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()
    render(
      <NotificationBell
        items={[]}
        unreadCount={0}
        open
        onToggle={() => {}}
        onClose={onClose}
        onSelect={() => {}}
        onMarkAllRead={() => {}}
      />,
    )
    await user.keyboard('{Escape}')
    expect(onClose).toHaveBeenCalledTimes(1)
  })
})
