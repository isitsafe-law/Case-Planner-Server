import { describe, expect, it } from 'vitest'
import { legalAssistantNamesForAttorneyChange } from '../App'

// Test-build feedback item: a case's Legal Assistant list auto-populates from whichever Staff
// Directory legal assistants list the newly-picked Assigned Attorney in their attorneyNames, but
// only while the case's current legal-assistant list is still empty - the same "only fill when
// currently blank" contract as districtForCountyChange (App.districtAutofill.test.ts), so a
// deliberate manual override (adding/removing/swapping assistants) is never clobbered by a later
// Assigned Attorney change. This is pure logic with no React rendering involved, so it's unit
// tested directly rather than through the (untested, 8000+ line) App component.
describe('legalAssistantNamesForAttorneyChange', () => {
  const directory = [
    { name: 'Tyler Story', attorneyNames: ['Stephen Lowman', 'Cody Eenigenburg'] },
    { name: 'Evelyn Allison', attorneyNames: ['Michael Bynum', 'Helen Newberry', 'Bailey Gambill'] },
    { name: 'Donna Ramsey', attorneyNames: ['Iván Martínez', 'Katie Meister'] },
  ]

  it('auto-fills with the single tied legal assistant when the list is blank', () => {
    expect(legalAssistantNamesForAttorneyChange([], 'Cody Eenigenburg', directory)).toEqual(['Tyler Story'])
    expect(legalAssistantNamesForAttorneyChange([], 'Helen Newberry', directory)).toEqual(['Evelyn Allison'])
  })

  it('auto-fills with every matching legal assistant when more than one ties to the attorney', () => {
    const ambiguous = [
      { name: 'Tyler Story', attorneyNames: ['Cody Eenigenburg'] },
      { name: 'Evelyn Allison', attorneyNames: ['Cody Eenigenburg'] },
    ]
    expect(legalAssistantNamesForAttorneyChange([], 'Cody Eenigenburg', ambiguous)).toEqual(['Tyler Story', 'Evelyn Allison'])
  })

  it('returns an empty list when the list is blank and the attorney has no tied legal assistant', () => {
    expect(legalAssistantNamesForAttorneyChange([], 'Michelle Davenport', directory)).toEqual([])
    expect(legalAssistantNamesForAttorneyChange([], 'Angela Dodson', directory)).toEqual([])
  })

  it('does not overwrite an already-populated list when the Assigned Attorney changes again', () => {
    expect(legalAssistantNamesForAttorneyChange(['Donna Ramsey'], 'Cody Eenigenburg', directory)).toEqual(['Donna Ramsey'])
  })

  it('does not overwrite a manually-built multi-assistant list', () => {
    expect(legalAssistantNamesForAttorneyChange(['Tyler Story', 'Evelyn Allison'], 'Iván Martínez', directory)).toEqual(['Tyler Story', 'Evelyn Allison'])
  })
})
