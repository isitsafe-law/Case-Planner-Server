import { describe, expect, it } from 'vitest'
import { assignableStaffNames } from '../App'

// Test-build feedback batch (task assignment): who a task on a case can be assigned to - the
// case's Assigned Attorney plus its Legal Assistant(s), replacing the old case_assignments-backed
// roster (always empty locally). Pure logic with no React rendering involved, so it's unit tested
// directly, mirroring legalAssistantNamesForAttorneyChange's test file.
describe('assignableStaffNames', () => {
  it('returns the attorney first, followed by each legal assistant', () => {
    expect(assignableStaffNames('Cody Eenigenburg', ['Tyler Story', 'Evelyn Allison'])).toEqual([
      'Cody Eenigenburg',
      'Tyler Story',
      'Evelyn Allison',
    ])
  })

  it('omits the attorney slot when the case has no assigned attorney', () => {
    expect(assignableStaffNames(null, ['Tyler Story'])).toEqual(['Tyler Story'])
    expect(assignableStaffNames(undefined, ['Tyler Story'])).toEqual(['Tyler Story'])
    expect(assignableStaffNames('   ', ['Tyler Story'])).toEqual(['Tyler Story'])
  })

  it('returns just the attorney when the case has no legal assistants', () => {
    expect(assignableStaffNames('Cody Eenigenburg', [])).toEqual(['Cody Eenigenburg'])
  })

  it('returns an empty list when the case has neither', () => {
    expect(assignableStaffNames(null, [])).toEqual([])
  })

  it('de-duplicates when the attorney also appears in the legal assistant list', () => {
    expect(assignableStaffNames('Cody Eenigenburg', ['Cody Eenigenburg', 'Tyler Story'])).toEqual([
      'Cody Eenigenburg',
      'Tyler Story',
    ])
  })

  it('trims whitespace and drops blank legal assistant names', () => {
    expect(assignableStaffNames(' Cody Eenigenburg ', ['  Tyler Story  ', '', '   '])).toEqual([
      'Cody Eenigenburg',
      'Tyler Story',
    ])
  })
})
