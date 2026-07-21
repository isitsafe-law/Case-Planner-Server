import { describe, expect, it } from 'vitest'
import { districtForCountyChange } from '../App'

// Multi-user rollout Phase 5 (reporting) data-capture: District auto-fills from County (a
// verified ARDOT county-to-district mapping) but stays a real, independently-editable dropdown -
// auto-fill only applies while District is still blank, so it never clobbers a deliberate manual
// override on a later County edit. This is pure logic with no React rendering involved, so it's
// unit-tested directly rather than through the (untested, 8000+ line) App component.
describe('districtForCountyChange', () => {
  it('auto-fills District from County when District is blank', () => {
    expect(districtForCountyChange(null, 'Crittenden')).toBe('District 1')
    expect(districtForCountyChange(undefined, 'Pulaski')).toBe('District 6')
    expect(districtForCountyChange('', 'Washington')).toBe('District 4')
  })

  it('covers counties spanning different districts', () => {
    expect(districtForCountyChange(null, 'Baxter')).toBe('District 9')
    expect(districtForCountyChange(null, 'Craighead')).toBe('District 10')
    expect(districtForCountyChange(null, 'Union')).toBe('District 7')
  })

  it('does not overwrite an already-set District when County changes', () => {
    expect(districtForCountyChange('District 3', 'Pulaski')).toBe('District 3')
  })

  it('returns null when District is blank and the county has no mapped district', () => {
    expect(districtForCountyChange(null, '')).toBeNull()
    expect(districtForCountyChange(null, 'Not A Real County')).toBeNull()
  })
})
