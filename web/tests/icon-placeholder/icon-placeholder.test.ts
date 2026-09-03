import { describe, expect, it } from 'vitest'
import { iconPlaceholderClass, SKELETON_FILL } from '~/utils/icon-placeholder'

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
