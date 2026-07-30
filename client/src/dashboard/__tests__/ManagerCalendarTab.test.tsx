import { describe, expect, it } from 'vitest'
import { CALENDAR_HORIZONS } from '../ManagerCalendarTab'

describe('ManagerCalendarTab horizons', () => {
  it('offers the complete long-range planning set and See All', () => {
    expect(CALENDAR_HORIZONS).toEqual([7, 30, 60, 90, 120, 180, 'all'])
  })
})
