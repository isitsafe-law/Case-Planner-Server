// Canonical date/date-time formatters (design-system/MASTER.md \7 and \8.15 "DateText"). This is
// the one implementation of formatDate/formatDateTime - App.tsx re-exports these as displayDate/
// displayDateTime aliases instead of keeping a second copy, and every other component that needs to
// render a date imports directly from here.

const MONTH_NAMES = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
]

export function formatDate(value?: string | null): string {
  if (!value) return '—'
  const match = value.match(/^(\d{4})-(\d{2})-(\d{2})/)
  if (!match) return value
  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  if (month < 1 || month > 12 || day < 1 || day > 31) return value
  return `${MONTH_NAMES[month - 1]} ${day}, ${year}`
}

const dateTimeFormatter = new Intl.DateTimeFormat('en-US', {
  timeZone: 'America/Chicago',
  month: 'long',
  day: 'numeric',
  year: 'numeric',
  hour: 'numeric',
  minute: '2-digit',
})

export function formatDateTime(value?: string | null): string {
  if (!value) return '—'
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) return value
  return `${dateTimeFormatter.format(parsed)} CT`
}
