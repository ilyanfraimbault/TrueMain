import { describe, expect, it } from 'vitest'
import { REGION_ITEMS, ALL } from '~/utils/filters'
import {
  TRACKED_REGIONS,
  TRACKED_REGIONS_LABEL,
  TRACKED_REGION_ITEMS,
  parseTrackedRegion,
} from '~~/shared/utils/regions'

describe('parseTrackedRegion', () => {
  it('matches case-insensitively and tolerates padding', () => {
    expect(parseTrackedRegion(' euw1 ')).toBe('EUW1')
    expect(parseTrackedRegion('Kr')).toBe('KR')
  })

  it('refuses an untracked or empty token rather than falling back', () => {
    expect(parseTrackedRegion('BR1')).toBeNull()
    expect(parseTrackedRegion('')).toBeNull()
    expect(parseTrackedRegion(null)).toBeNull()
  })
})

describe('the tracked-region list', () => {
  it('drives the select options and the operator-facing label', () => {
    expect(TRACKED_REGION_ITEMS).toEqual(TRACKED_REGIONS.map(r => ({ label: r, value: r })))
    expect(TRACKED_REGIONS_LABEL).toBe(TRACKED_REGIONS.join(' · '))
  })

  // The bug this issue fixes: the filter selects and the bulk-seed parser used
  // to hold two hand-written copies of the same list, so a shard added to one
  // was refused by the other.
  it('is the same list the filter selects offer, behind the "all" sentinel', () => {
    expect(REGION_ITEMS[0]).toEqual({ label: 'All regions', value: ALL })
    expect(REGION_ITEMS.slice(1).map(item => item.value)).toEqual([...TRACKED_REGIONS])
  })
})
