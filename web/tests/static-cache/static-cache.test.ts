import { describe, expect, it } from 'vitest'
import {
  getStaticCachedData,
  markStaticFetched,
  STATIC_CACHE_TTL_MS,
  type PayloadHost,
} from '~~/app/utils/static-cache'

function makeHost(): PayloadHost {
  return { payload: { data: {} }, static: { data: {} } }
}

describe('getStaticCachedData', () => {
  it('returns undefined when nothing is cached', () => {
    expect(getStaticCachedData('rune-tree', makeHost())).toBeUndefined()
  })

  it('returns the cached value when the timestamp is fresh', () => {
    const host = makeHost()
    host.payload.data['rune-tree'] = { styles: [] }
    markStaticFetched('rune-tree', host, 1_000)
    expect(getStaticCachedData('rune-tree', host, 1_000 + 5_000)).toEqual({ styles: [] })
  })

  it('returns undefined once the entry exceeds the TTL', () => {
    const host = makeHost()
    host.payload.data['rune-tree'] = { styles: [] }
    markStaticFetched('rune-tree', host, 0)
    expect(getStaticCachedData('rune-tree', host, STATIC_CACHE_TTL_MS + 1)).toBeUndefined()
  })

  it('treats SSR-hydrated entries without a timestamp as fresh', () => {
    // First client tick after SSR: Nuxt has populated payload.data but the
    // sibling stamp does not exist yet. We must not bypass that hydration.
    const host = makeHost()
    host.payload.data['rune-tree'] = { styles: [] }
    expect(getStaticCachedData('rune-tree', host)).toEqual({ styles: [] })
  })

  it('falls back to nuxtApp.static.data on subsequent navigations', () => {
    // After hydration Nuxt moves resolved data into `static.data`; the helper
    // must keep finding the value there or every cross-navigation would refetch.
    const host = makeHost()
    host.static.data['champion-static-list'] = [{ championId: 1 }]
    host.static.data['champion-static-list::fetchedAt'] = 500
    expect(getStaticCachedData('champion-static-list', host, 1_000)).toEqual([{ championId: 1 }])
  })

  it('prefers payload.data over static.data when both are present', () => {
    const host = makeHost()
    host.static.data['rune-tree'] = { styles: ['stale'] }
    host.payload.data['rune-tree'] = { styles: ['fresh'] }
    expect(getStaticCachedData('rune-tree', host)).toEqual({ styles: ['fresh'] })
  })
})

describe('markStaticFetched', () => {
  it('writes the timestamp to the sibling key', () => {
    const host = makeHost()
    markStaticFetched('rune-tree', host, 1234)
    expect(host.payload.data['rune-tree::fetchedAt']).toBe(1234)
  })
})
