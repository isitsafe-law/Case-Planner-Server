import { useState } from 'react'
import { Btn } from '../ui/Btn'

export type DashboardWorkActionItem = {
  key: string
  type: 'task' | 'deadline' | 'discovery' | 'service'
  caseId: number
  dueDate?: string | null
  tab: string
}

export function DashboardWorkActions({
  item,
  onOpen,
  onComplete,
  onSaveDueDate,
  onService,
  onDiscovery,
  showOpen = true,
}: {
  item: DashboardWorkActionItem
  onOpen: () => void
  onComplete?: () => Promise<void>
  onSaveDueDate?: (dueDate: string) => Promise<void>
  onService?: () => Promise<void>
  onDiscovery?: () => Promise<void>
  showOpen?: boolean
}) {
  const [dueDate, setDueDate] = useState(item.dueDate ?? '')
  const [editingDate, setEditingDate] = useState(false)
  const [busy, setBusy] = useState(false)

  async function run(action: () => Promise<void>) {
    setBusy(true)
    try {
      await action()
      setEditingDate(false)
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
      <details className="dashboard-work-actions-menu">
        <summary aria-label="More work actions">More</summary>
        <div className="dashboard-work-actions-popover">
          {(item.type === 'task' || item.type === 'deadline') && onSaveDueDate && (
            editingDate ? (
              <span className="inline-quick-form compact-actions">
                <input type="date" value={dueDate} onChange={(event) => setDueDate(event.currentTarget.value)} aria-label="New due date" />
                <Btn size="sm" onClick={() => void run(() => onSaveDueDate(dueDate))} disabled={busy || !dueDate}>Save date</Btn>
                <Btn size="sm" variant="ghost" onClick={() => { setDueDate(item.dueDate ?? ''); setEditingDate(false) }}>Cancel</Btn>
              </span>
            ) : <Btn size="sm" variant="ghost" onClick={() => setEditingDate(true)}>Change due date</Btn>
          )}
          {showOpen && <Btn size="sm" variant="ghost" onClick={onOpen}>Open case</Btn>}
        </div>
      </details>
    </div>
  )
}
