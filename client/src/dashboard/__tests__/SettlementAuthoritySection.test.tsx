import { describe, expect, it } from 'vitest'
import { canDecideSettlementAuthority, daysPending, settlementAuthorityDelta, settlementAuthorityDeltaPercent } from '../SettlementAuthoritySection'

describe('settlementAuthorityDelta', () => {
  it('computes requested minus the Estimate of Just Compensation deposit', () => {
    expect(settlementAuthorityDelta(150000, 100000)).toBe(50000)
  })

  it('returns null when there is no deposit amount to compare against (null, not zero-divide)', () => {
    expect(settlementAuthorityDelta(150000, null)).toBeNull()
    expect(settlementAuthorityDelta(150000, undefined)).toBeNull()
  })

  it('allows a zero deposit amount to still compute a delta (only null/undefined short-circuits)', () => {
    expect(settlementAuthorityDelta(150000, 0)).toBe(150000)
  })
})

describe('settlementAuthorityDeltaPercent', () => {
  it('computes delta as a percentage of the deposit amount', () => {
    expect(settlementAuthorityDeltaPercent(150000, 100000)).toBe(50)
  })

  it('returns null for a null, undefined, or zero deposit amount (avoids divide-by-zero/NaN)', () => {
    expect(settlementAuthorityDeltaPercent(150000, null)).toBeNull()
    expect(settlementAuthorityDeltaPercent(150000, undefined)).toBeNull()
    expect(settlementAuthorityDeltaPercent(150000, 0)).toBeNull()
  })

  it('supports a negative delta (requested less than the deposit)', () => {
    expect(settlementAuthorityDeltaPercent(80000, 100000)).toBe(-20)
  })
})

describe('daysPending', () => {
  it('floors whole days between requestedAt and now', () => {
    const now = new Date('2026-07-27T12:00:00Z')
    expect(daysPending('2026-07-20T12:00:00Z', now)).toBe(7)
    expect(daysPending('2026-07-27T06:00:00Z', now)).toBe(0)
  })

  it('never returns a negative count for a future-dated requestedAt', () => {
    const now = new Date('2026-07-27T12:00:00Z')
    expect(daysPending('2026-07-28T12:00:00Z', now)).toBe(0)
  })
})

describe('canDecideSettlementAuthority', () => {
  it('allows the decide action when there is no authenticated user (local/no-auth mode)', () => {
    expect(canDecideSettlementAuthority(null)).toBe(true)
  })

  it('allows the decide action only for Chief Counsel', () => {
    expect(canDecideSettlementAuthority({ managerTier: 'ChiefCounsel' } as any)).toBe(true)
    expect(canDecideSettlementAuthority({ managerTier: 'DeputyChiefCounsel' } as any)).toBe(false)
    expect(canDecideSettlementAuthority({ managerTier: null } as any)).toBe(false)
  })
})
