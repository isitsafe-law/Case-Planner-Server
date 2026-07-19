import type { StatusTone } from './StatusChip'

export function StatusSelect({
  value,
  options,
  tone,
  onChange,
  ariaLabel,
}: {
  value: string
  options: readonly string[]
  tone: StatusTone
  onChange: (value: string) => void
  ariaLabel: string
}) {
  return (
    <select
      className={`ui-status ui-status-select ui-status-${tone}`}
      value={value}
      aria-label={ariaLabel}
      onChange={(event) => onChange(event.target.value)}
    >
      {options.map((option) => (
        <option key={option} value={option}>
          {option}
        </option>
      ))}
    </select>
  )
}
