import { useState } from 'react'
import { ModalShell } from '../App'
import { PIPELINE_HOLDERS } from './types'

export function PipelineHandoffDialog({
  caseName,
  onClose,
  onSubmit,
}: {
  caseName: string
  onClose: () => void
  onSubmit: (payload: { newHolder: string; handoffDate: string; followUpDate: string }) => Promise<void>
}) {
  const today = new Date().toISOString().slice(0, 10)
  const [newHolder, setNewHolder] = useState('')
  const [followUpDate, setFollowUpDate] = useState('')
  const [busy, setBusy] = useState(false)

  return (
    <ModalShell title={`Pipeline Handoff: ${caseName}`} onClose={onClose}>
      <form
        className="stacked-form"
        onSubmit={async (e) => {
          e.preventDefault()
          setBusy(true)
          try {
            await onSubmit({ newHolder, handoffDate: today, followUpDate })
          } finally {
            setBusy(false)
          }
        }}
      >
        <label>
          Send to
          <select value={newHolder} onChange={(e) => setNewHolder(e.currentTarget.value)} required>
            <option value="">Select...</option>
            {PIPELINE_HOLDERS.map((h) => <option key={h} value={h}>{h}</option>)}
          </select>
        </label>
        <label>
          Follow-up date
          <input type="date" value={followUpDate} onChange={(e) => setFollowUpDate(e.currentTarget.value)} required />
        </label>
        <div className="button-row">
          <button className="primary" type="submit" disabled={busy}>Send</button>
          <button type="button" onClick={onClose}>Cancel</button>
        </div>
      </form>
    </ModalShell>
  )
}
