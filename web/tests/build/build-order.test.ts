import type { MatchDetailItemEvent } from '~~/shared/types/match-detail'
import type { StaticItemData } from '~~/shared/types/static-data'
import { describe, expect, it } from 'vitest'
import { isBuildOrderEvent, resolveEventItemId } from '~~/shared/utils/build'

function ev(partial: Partial<MatchDetailItemEvent> & { eventType: string }): MatchDetailItemEvent {
  return { timestampMs: 0, itemId: 0, beforeId: null, afterId: null, ...partial }
}

function item(id: number, extra: Partial<StaticItemData> = {}): StaticItemData {
  return { id, name: `Item ${id}`, iconUrl: '', totalGold: 0, ...extra }
}

// A shop item (Boots), an auto-transform (empowered-recall boots: not
// purchasable), and a quest stage that stays purchasable but leaves the store.
const CATALOG: Record<number, StaticItemData> = {
  1001: item(1001, { purchasable: true }),
  3013: item(3013, { purchasable: false, inStore: false }),
  3866: item(3866, { purchasable: true, inStore: false }),
}

describe('resolveEventItemId', () => {
  it('uses itemId when present', () => {
    expect(resolveEventItemId(ev({ eventType: 'ITEM_PURCHASED', itemId: 1001 }))).toBe(1001)
  })

  it('falls back to beforeId then afterId for undo events (itemId = 0)', () => {
    expect(resolveEventItemId(ev({ eventType: 'ITEM_UNDO', itemId: 0, beforeId: 3006 }))).toBe(3006)
    expect(resolveEventItemId(ev({ eventType: 'ITEM_UNDO', itemId: 0, beforeId: null, afterId: 3020 }))).toBe(3020)
  })

  it('returns 0 when nothing resolves', () => {
    expect(resolveEventItemId(ev({ eventType: 'ITEM_UNDO' }))).toBe(0)
  })
})

describe('isBuildOrderEvent', () => {
  it('keeps a normal shop purchase', () => {
    expect(isBuildOrderEvent(ev({ eventType: 'ITEM_PURCHASED', itemId: 1001 }), CATALOG)).toBe(true)
  })

  it('drops an auto-transform purchase (non-purchasable item)', () => {
    expect(isBuildOrderEvent(ev({ eventType: 'ITEM_PURCHASED', itemId: 3013 }), CATALOG)).toBe(false)
  })

  it('drops a purchase whose item left the store (inStore = false)', () => {
    expect(isBuildOrderEvent(ev({ eventType: 'ITEM_PURCHASED', itemId: 3866 }), CATALOG)).toBe(false)
  })

  it('keeps a purchase of an item absent from the catalog', () => {
    expect(isBuildOrderEvent(ev({ eventType: 'ITEM_PURCHASED', itemId: 99999 }), CATALOG)).toBe(true)
  })

  it('drops every ITEM_DESTROYED, even for a purchasable item (the transform half)', () => {
    expect(isBuildOrderEvent(ev({ eventType: 'ITEM_DESTROYED', itemId: 1001 }), CATALOG)).toBe(false)
  })

  it('keeps deliberate sells and undos', () => {
    expect(isBuildOrderEvent(ev({ eventType: 'ITEM_SOLD', itemId: 1001 }), CATALOG)).toBe(true)
    expect(isBuildOrderEvent(ev({ eventType: 'ITEM_UNDO', itemId: 0, beforeId: 1001 }), CATALOG)).toBe(true)
  })
})
