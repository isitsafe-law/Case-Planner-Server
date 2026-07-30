// Single user-facing event catalog shared by case Events and dashboard calendars.
// Legacy/custom records remain readable, but new entry and filters use this list.
export const CASE_EVENT_TYPES = ['Jury Trial', 'Hearing', 'Deposition', 'Mediation', 'Filing Deadline', 'Other'] as const
export type CaseEventType = typeof CASE_EVENT_TYPES[number]
