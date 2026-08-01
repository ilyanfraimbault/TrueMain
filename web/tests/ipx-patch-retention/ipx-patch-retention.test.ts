import { describe, expect, it } from 'vitest'
import { createBoundedByteCache } from '~~/server/utils/bounded-byte-cache'
import { createPatchRetention, isOutsideRetention, parsePatchRank } from '~~/server/utils/ipx-patch-retention'

// Shapes of the real `/_ipx/**` cache keys (`event.path`), which is what the
// retention parses.
const ddragon = (patch: string, file = 'champion/Annie.png') =>
  `/_ipx/f_webp,s_64x64/https://ddragon.leagueoflegends.com/cdn/${patch}/img/${file}`
const communityDragon = (patch: string) =>
  `/_ipx/f_webp,s_64x64/https://raw.communitydragon.org/${patch}/plugins/rcp-be-lol-game-data/global/default/v1/perk-images/statmods/statmodstenacityicon.png`

describe('parsePatchRank', () => {
  it('ranks a Data Dragon key by its patch, ignoring the hotfix segment', () => {
    expect(parsePatchRank(ddragon('16.15.1'))).toBe(parsePatchRank(ddragon('16.15.3')))
  })

  it('gives a Community Dragon key the same rank as the Data Dragon one', () => {
    expect(parsePatchRank(communityDragon('16.15'))).toBe(parsePatchRank(ddragon('16.15.1')))
  })

  it('orders patches, including across a season rollover', () => {
    expect(parsePatchRank(ddragon('16.15.1'))!).toBeGreaterThan(parsePatchRank(ddragon('16.2.1'))!)
    expect(parsePatchRank(ddragon('16.1.1'))!).toBeGreaterThan(parsePatchRank(ddragon('15.24.1'))!)
  })

  it('returns null for keys that carry no patch', () => {
    expect(parsePatchRank('/_ipx/s_64x64/positions/icon-position-top.png')).toBeNull()
    expect(parsePatchRank(
      '/_ipx/s_26x26/https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-static-assets/global/default/images/ranked-mini-crests/gold.svg',
    )).toBeNull()
  })
})

describe('createPatchRetention', () => {
  it('sweeps only on the first key of a newer patch', () => {
    const retention = createPatchRetention()
    expect(retention.observe(ddragon('16.15.1'))).not.toBeNull()
    expect(retention.observe(ddragon('16.15.1', 'item/3157.png'))).toBeNull()
    expect(retention.observe(communityDragon('16.15'))).toBeNull()
  })

  it('never sweeps on a patchless key', () => {
    const retention = createPatchRetention()
    expect(retention.observe('/_ipx/s_64x64/positions/icon-position-top.png')).toBeNull()
  })

  it('retains the current patch and the two before it', () => {
    const retention = createPatchRetention()
    for (const patch of ['16.12.1', '16.13.1', '16.14.1']) retention.observe(ddragon(patch))
    const retained = retention.observe(ddragon('16.15.1'))!

    expect(isOutsideRetention(ddragon('16.15.1'), retained)).toBe(false)
    expect(isOutsideRetention(ddragon('16.14.1'), retained)).toBe(false)
    expect(isOutsideRetention(ddragon('16.13.1'), retained)).toBe(false)
    expect(isOutsideRetention(ddragon('16.12.1'), retained)).toBe(true)
  })

  it('keeps the three newest across a season rollover rather than doing newest-minus-2 arithmetic', () => {
    const retention = createPatchRetention()
    for (const patch of ['15.23.1', '15.24.1', '16.1.1']) retention.observe(ddragon(patch))
    const retained = retention.observe(ddragon('16.2.1'))!

    // 15.24 is two patches back, not ~1000, so it survives.
    expect(isOutsideRetention(ddragon('15.24.1'), retained)).toBe(false)
    expect(isOutsideRetention(ddragon('16.1.1'), retained)).toBe(false)
    expect(isOutsideRetention(ddragon('15.23.1'), retained)).toBe(true)
  })

  it('does not sweep when an older patch is requested, so browsing an old patch stays cacheable', () => {
    const retention = createPatchRetention()
    retention.observe(ddragon('16.15.1'))
    // The champion page's patch filter can ask for anything still on the CDN.
    expect(retention.observe(ddragon('16.4.1'))).toBeNull()
  })

  it('leaves patchless keys in the cache when a sweep runs', () => {
    const retention = createPatchRetention()
    retention.observe(ddragon('16.12.1'))
    const retained = retention.observe(ddragon('16.15.1'))!

    expect(isOutsideRetention('/_ipx/s_64x64/positions/icon-position-top.png', retained)).toBe(false)
  })
})

describe('patch retention against the byte cache', () => {
  it('reclaims the bytes of an expired patch and spares the rest', () => {
    const cache = createBoundedByteCache<{ byteLength: number }>({ maxBytes: 1000, maxEntryBytes: 1000 })
    const retention = createPatchRetention()

    const stale = ddragon('16.11.1')
    const positions = '/_ipx/s_64x64/positions/icon-position-top.png'
    for (const key of [stale, ddragon('16.12.1'), ddragon('16.13.1'), positions]) {
      retention.observe(key)
      cache.set(key, { byteLength: 10 })
    }
    expect(cache.bytes).toBe(40)

    const retained = retention.observe(ddragon('16.14.1'))!
    expect(cache.purge(key => isOutsideRetention(key, retained))).toBe(1)
    expect(cache.get(stale)).toBeUndefined()
    expect(cache.get(positions)).toBeDefined()
    expect(cache.bytes).toBe(30)
  })
})
