import { useEffect, useState } from 'react'
import type { DeadlineItem } from '../App'

export function DeadlineDueDateEditor({ item, onSave }: { item: DeadlineItem; onSave: (dueDate: string, reason?: string) => Promise<void> }) {
  const [dueDate, setDueDate] = useState(item.dueDate || '')
  const [reason, setReason] = useState('')
  const generated = !item.isManual

  useEffect(() => { setDueDate(item.dueDate || ''); setReason('') }, [item.dueDate])

  if (!generated) return <input type="date" className="inline-edit-input" value={dueDate} aria-label={`Due date for ${item.title}`} title="Direct date edit" onChange={(event) => { setDueDate(event.target.value); void onSave(event.target.value) }} />

  return <span className="deadline-override-editor">
    <input type="date" className="inline-edit-input" value={dueDate} aria-label={`Due date for ${item.title}`} title="Generated deadline: saving creates a manual override" onChange={(event) => setDueDate(event.target.value)} />
    <input type="text" className="inline-edit-input" value={reason} placeholder="Reason for override" aria-label={`Reason for changing ${item.title}`} onChange={(event) => setReason(event.target.value)} />
    <button type="button" onClick={() => void onSave(dueDate, reason.trim() || undefined)} disabled={!dueDate}>Save date</button>
  </span>
}
