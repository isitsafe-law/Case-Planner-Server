import { describe, expect, it } from 'vitest'
import { attorneyOptions } from '../App'

// Staff Directory prerequisite (multi-user rollout Phase 5, reporting): Assigned Attorney becomes
// a fixed dropdown sourced from the Staff Directory's active attorneys, but any existing case's
// assignedAttorney value that isn't currently an active attorney name must stay selectable rather
// than being silently hidden - same grandfathering shape as districtForCountyChange/countyOptions,
// just parameterized on a dynamic (server-loaded) name list instead of a hardcoded const array.
// Pure logic, no React rendering, so unit-tested directly per that file's precedent.
describe('attorneyOptions', () => {
  const activeNames = ['Michelle Davenport', 'Angela Dodson', 'Helen Newberry']

  it('returns the active attorney list unchanged when the current value is already in it', () => {
    expect(attorneyOptions(activeNames, 'Helen Newberry')).toEqual(activeNames)
  })

  it('returns the active attorney list unchanged when there is no current value', () => {
    expect(attorneyOptions(activeNames, null)).toEqual(activeNames)
    expect(attorneyOptions(activeNames, undefined)).toEqual(activeNames)
    expect(attorneyOptions(activeNames, '')).toEqual(activeNames)
  })

  it('prepends a legacy/free-text value that is not in the active list, without dropping it', () => {
    expect(attorneyOptions(activeNames, 'Old Retired Attorney')).toEqual(['Old Retired Attorney', ...activeNames])
  })

  it('prepends a name that was deactivated in the directory after being assigned to a case', () => {
    const withoutBynum = ['Michelle Davenport', 'Angela Dodson']
    expect(attorneyOptions(withoutBynum, 'Michael Bynum')).toEqual(['Michael Bynum', ...withoutBynum])
  })

  it('trims whitespace before comparing against the active list', () => {
    expect(attorneyOptions(activeNames, '  Helen Newberry  ')).toEqual(activeNames)
  })
})
