import { describe, expect, it } from 'vitest'
import { STATIC_DATA_CACHE_CONTROL, staticDataCacheControl } from '~~/server/utils/static-cache-headers'

// Static game data is cached by the browser across reloads, and nothing else is (#1584).
describe('staticDataCacheControl', () => {
  it.each([
    '/api/static/items?patch=16.18',
    '/api/static/rune-tree',
    '/api/static/103?patch=16.18',
    '/api/static/champion-slugs',
  ])('caches a successful %s', (path) => {
    expect(staticDataCacheControl(path, 200)).toBe(STATIC_DATA_CACHE_CONTROL)
  })

  it.each([404, 500, 502, 304])('never caches a %i', (status) => {
    expect(staticDataCacheControl('/api/static/items', status)).toBeNull()
  })

  it.each([
    '/api/champions/103',
    '/api/truemains',
    '/champions/ahri',
    '/api/statics-lookalike',
    undefined,
  ])('leaves %s alone', (path) => {
    expect(staticDataCacheControl(path, 200)).toBeNull()
  })
})
