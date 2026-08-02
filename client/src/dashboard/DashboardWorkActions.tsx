import { useEffect, useState } from 'react'
import { Btn } from '../ui/Btn'
import { formatDate } from '../ui/format'

export type DashboardWorkActionItem = {
  key: string
  type: 'task' | 'deadline' | 'discovery' | 'service'
  caseId: number
  title: string
  dueDate?: string | null
  tab: string
}

export function DashboardWorkActions({
  item,
  onComplete,
  onService,
  onDiscovery,
}: {
  item: DashboardWorkActionItem
  onComplete?: () => Promise<void>
  onService?: () => Promise<void>
  onDiscovery?: () => Promise<void>
}) {
  const [busy, setBusy] = useState(false)

  async function run(action: () => Promise<void>) {
    setBusy(true)
    try {
      await action()
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="ui-row-actions dashboard-work-actions">
      {item.type === 'task' && onComplete && <Btn size="sm" onClick={() => void run(onComplete)}>Mark done</Btn>}
      {item.type === 'deadline' && onComplete && <Btn size="sm" onClick={() => void run(onComplete)}>Complete</Btn>}
      {item.type === 'discovery' && onDiscovery && <Btn size="sm" onClick={() => void run(onDiscovery)}>Record response</Btn>}
      {item.type === 'service' && onService && <Btn size="sm" onClick={() => void run(onService)}>Update service</Btn>}
      {busy && <span className="ui-sub" aria-live="polite">Saving…</span>}
    </div>
  )
}

export function DashboardDueDate({
  item,
  onSave,
}: {
  item: DashboardWorkActionItem
  onSave?: (dueDate: string) => Promise<void>
}) {
  const [dueDate, setDueDate] = useState(item.dueDate ?? '')
  const [editing, setEditing] = useState(false)
  const [busy, setBusy] = useState(false)
  const editable = (item.type === 'task' || item.type === 'deadline') && Boolean(onSave)
  useEffect(() => { setDueDate(item.dueDate ?? '') }, [item.dueDate])

  if (!editable) return <span className="ui-data">{formatDate(item.dueDate)}</span>
  if (!editing) return <button type="button" className="dashboard-due-date-link ui-data" onClick={() => setEditing(true)} aria-label={`Change due date for ${item.title}`}>{formatDate(item.dueDate)}</button>

  async function save() {
    if (!dueDate || !onSave) return
    setBusy(true)
    try {
      await onSave(dueDate)
      setEditing(false)
    } finally {
      setBusy(false)
    }
  }

  return <span className="inline-quick-form compact-actions dashboard-due-date-editor">
    <input type="date" value={dueDate} onChange={(event) => setDueDate(event.currentTarget.value)} aria-label={`New due date for ${item.title}`} />
    <Btn size="sm" onClick={() => void save()} disabled={busy || !dueDate}>Save</Btn>
    <Btn size="sm" variant="ghost" onClick={() => { setDueDate(item.dueDate ?? ''); setEditing(false) }} disabled={busy}>Cancel</Btn>
  </span>
}
