import { describe, expect, it } from 'vitest'
import { linkedAccountDisplayName } from '../App'

// Notifications gap fix: the Staff Directory's "Linked Account" dropdown needs to show a real name
// for an already-set linkedUserId even before staffRoster has been loaded (it's empty until "Load
// Staff" is clicked, or always empty locally where Entra is disabled) - pure lookup, no React
// rendering involved, so it's unit tested directly, mirroring assignableStaffNames' test file.
describe('linkedAccountDisplayName', () => {
  const staffRoster = [
    { id: 'user-1', displayName: 'Cody Eenigenburg' },
    { id: 'user-2', displayName: 'Katie Meister' },
  ]

  it('returns null when there is no linked user id', () => {
    expect(linkedAccountDisplayName(null, staffRoster)).toBeNull()
    expect(linkedAccountDisplayName(undefined, staffRoster)).toBeNull()
    expect(linkedAccountDisplayName('', staffRoster)).toBeNull()
  })

  it('resolves a linked user id to its display name when present in the roster', () => {
    expect(linkedAccountDisplayName('user-1', staffRoster)).toBe('Cody Eenigenburg')
    expect(linkedAccountDisplayName('user-2', staffRoster)).toBe('Katie Meister')
  })

  it('falls back to the raw id when the roster has not been loaded yet', () => {
    expect(linkedAccountDisplayName('user-1', [])).toBe('user-1')
  })

  it('falls back to the raw id when the linked id no longer matches anyone on the roster', () => {
    expect(linkedAccountDisplayName('user-9-deactivated', staffRoster)).toBe('user-9-deactivated')
  })
})
