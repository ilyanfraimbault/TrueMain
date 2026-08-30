import { afterEach, describe, expect, it, vi } from 'vitest'
import { ALL } from '~/utils/filters'
import { SINCE_ITEMS, WINDOW_MS, sinceToIso } from '~/utils/time-windows'

// The logs, processes and crashes panels all send their "Since" select straight to the
// backend through `sinceToIso`. Two things have to hold: the derived bound really is the
// window the label promises, and every option the select offers has a duration behind it.

describe('sinceToIso', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it.each([
    ['1h', '2026-06-09T11:00:00.000Z'],
    ['24h', '2026-06-08T12:00:00.000Z'],
    ['7d', '2026-06-02T12:00:00.000Z'],
    ['30d', '2026-05-10T12:00:00.000Z'],
  ])('derives the %s lower bound from now', (windowKey, expected) => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-06-09T12:00:00.000Z'))

    expect(sinceToIso(windowKey)).toBe(expected)
  })

  it('emits UTC, which is what the backend parses the bound as', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-06-09T12:00:00.000Z'))

    // A local-time string would silently shift the window by the operator's offset.
    expect(sinceToIso('24h')).toMatch(/Z$/)
  })
})

describe('SINCE_ITEMS', () => {
  it('offers exactly the windows that have a duration, plus the all-time sentinel', () => {
    // This is the invariant that keeps `sinceToIso` safe: it looks its argument up in
    // `WINDOW_MS` without a guard, so an option in the select with no matching duration
    // would produce an unusable bound. Adding one without the other fails here.
    const [allTime, ...windows] = SINCE_ITEMS

    expect(allTime!.value).toBe(ALL)
    expect(windows.map(item => item.value)).toEqual(Object.keys(WINDOW_MS))
  })

  it('lists the windows shortest-first, and every one is longer than the last', () => {
    const durations = SINCE_ITEMS
      .filter(item => item.value !== ALL)
      .map(item => WINDOW_MS[item.value]!)

    expect(durations).toEqual([...durations].sort((a, b) => a - b))
    expect(new Set(durations).size).toBe(durations.length)
  })
})
