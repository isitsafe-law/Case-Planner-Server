import { useState } from 'react'
import { ModalShell } from '../App'
import { PIPELINE_HOLDERS, PIPELINE_STAGES } from './types'

export function PipelineHandoffDialog({
  caseName,
  onClose,
  onSubmit,
}: {
  caseName: string
  onClose: () => void
  onSubmit: (payload: { newHolder: string; newStage: string; handoffDate: string; nextReviewDate: string; note: string }) => Promise<void>
}) {
  const today = new Date().toISOString().slice(0, 10)
  const [newHolder, setNewHolder] = useState('')
  const [newStage, setNewStage] = useState('')
  const [handoffDate, setHandoffDate] = useState(today)
  const [nextReviewDate, setNextReviewDate] = useState('')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)

  return (
    <ModalShell title={`Pipeline Handoff: ${caseName}`} onClose={onClose}>
      <form
        className="stacked-form"
        onSubmit={async (e) => {
          e.preventDefault()
          setBusy(true)
          try {
            await onSubmit({ newHolder, newStage, handoffDate, nextReviewDate, note })
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
          New stage
          <select value={newStage} onChange={(e) => setNewStage(e.currentTarget.value)} required>
            <option value="">Select...</option>
            {PIPELINE_STAGES.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </label>
        <label>
          Date sent
          <input type="date" value={handoffDate} onChange={(e) => setHandoffDate(e.currentTarget.value)} required />
        </label>
        <label>
          Next review date
          <input type="date" value={nextReviewDate} onChange={(e) => setNextReviewDate(e.currentTarget.value)} />
        </label>
        <label>
          Note (optional)
          <textarea value={note} onChange={(e) => setNote(e.currentTarget.value)} rows={2} />
        </label>
        <div className="button-row">
          <button className="primary" type="submit" disabled={busy}>Send</button>
          <button type="button" onClick={onClose}>Cancel</button>
        </div>
      </form>
    </ModalShell>
  )
}
