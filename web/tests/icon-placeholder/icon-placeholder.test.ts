import { describe, expect, it } from 'vitest'
import { iconPlaceholderClass, isIconUnresolved, SKELETON_FILL } from '~/utils/icon-placeholder'

describe('iconPlaceholderClass', () => {
  it('pulses the shared skeleton fill while loading', () => {
    const classes = iconPlaceholderClass(false)

    expect(classes).toContain('animate-pulse')
    // Must stay the fill `ui.skeleton.base` sets in app.config.ts: this is the
    // most numerous skeleton on the site, so a drift here is what "loading"
    // looks like everywhere.
    expect(classes).toContain(SKELETON_FILL)
  })

  it('draws a failed icon hollow, and still', () => {
    const classes = iconPlaceholderClass(true)

    expect(classes).not.toContain('animate-pulse')
    expect(classes).toContain('ring-inset')
  })

  it('makes the two states differ by more than motion', () => {
    // The regression this exists for: when the states differed only by
    // `animate-pulse`, a page whose icons had all failed read as one still
    // loading, which is how the 1.20.0 /_ipx outage presented.
    const loading = new Set(iconPlaceholderClass(false).split(' '))
    const failed = new Set(iconPlaceholderClass(true).split(' '))
    const distinguishing = [...failed].filter(c => !loading.has(c) && c !== 'animate-pulse')

    expect(distinguishing.length).toBeGreaterThan(0)
    expect(failed.has(SKELETON_FILL)).toBe(false)
  })
})

describe('isIconUnresolved', () => {
  it('is false when the caller has not said the source is final', () => {
    // The guard that keeps this opt-in. Most call sites never wire their fetch
    // state — `ChampionBuildTabs`' leading item icon is rendered on ids alone,
    // because the item map is a deferred client-only fetch that lands after the
    // builds — and they must keep the loading box through that window. Treating
    // "said nothing" as "settled" would flash a hollow failed box on every one.
    expect(isIconUnresolved(false, false)).toBe(false)
  })

  it('is false once there is a source', () => {
    expect(isIconUnresolved(true, false)).toBe(false)
    expect(isIconUnresolved(true, true)).toBe(false)
  })

  it('is true when the source is final and still empty', () => {
    // The case this exists for: a static-data fetch that *failed* rather than
    // one still in flight. The map then resolves no id, so every icon it backs
    // is sourceless for good — it must read as an empty slot, not as one still
    // filling, and not as the raw Riot id it could not resolve.
    expect(isIconUnresolved(false, true)).toBe(true)
  })

  it('draws that state the same as a failed image', () => {
    expect(iconPlaceholderClass(isIconUnresolved(false, true)))
      .toBe(iconPlaceholderClass(true))
  })
})
