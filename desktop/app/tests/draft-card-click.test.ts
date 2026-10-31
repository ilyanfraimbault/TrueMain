import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { onBeforeUnmount, ref, watch } from 'vue'
import type { Availability, ChampSelectKind } from '~/composables/useChampSelectActions'
import { ARMED_MS, useDraftCardClick } from '~/composables/useDraftCardClick'

/**
 * A click on a suggested champion (#2381): during our pick it hovers the
 * champion, an ally's pick intent asks by arming the card; anywhere else it
 * previews the build.
 */

function setup(options: { mode?: 'pick' | 'ban', kind?: ChampSelectKind | null, availability?: Availability, ally?: boolean, writable?: boolean } = {}) {
  const hover = vi.fn()
  const preview = vi.fn()
  vi.stubGlobal('useChampSelectActions', () => ({
    writable: ref(options.writable ?? true),
    kind: ref(options.kind === undefined ? 'pick' : options.kind),
    pending: ref(null),
    availability: () => options.availability ?? { allowed: true, reason: null },
    allyHovering: () => (options.ally ? { position: 'JUNGLE' } : null),
    allyName: () => 'your Jungle',
    hover,
  }))
  const card = useDraftCardClick(() => options.mode ?? 'pick', preview)
  return { card, hover, preview }
}

beforeEach(() => {
  vi.useFakeTimers()
  vi.stubGlobal('ref', ref)
  vi.stubGlobal('watch', watch)
  vi.stubGlobal('onBeforeUnmount', onBeforeUnmount)
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('useDraftCardClick', () => {
  it('hovers on one click during our pick', () => {
    const { card, hover, preview } = setup()
    expect(card.hovers(105)).toBe(true)
    card.click(105)
    expect(hover).toHaveBeenCalledWith(105)
    expect(preview).not.toHaveBeenCalled()
    expect(card.status(105)).toBeNull()
  })

  it('previews the build out of turn, during a ban, or outside the shell', () => {
    for (const options of [{ kind: null }, { mode: 'ban' as const, kind: 'ban' as const }, { writable: false }]) {
      const { card, hover, preview } = setup(options)
      expect(card.hovers(105)).toBe(false)
      card.click(105)
      expect(hover).not.toHaveBeenCalled()
      expect(preview).toHaveBeenCalledWith(105)
    }
  })

  it('previews the build of the champion the client already shows hovered, and says so', () => {
    const { card, hover, preview } = setup({ availability: { allowed: false, reason: 'Hovered in the client' } })
    card.click(105)
    expect(hover).not.toHaveBeenCalled()
    expect(preview).toHaveBeenCalledWith(105)
    expect(card.status(105)).toEqual({ text: 'Hovered', tone: 'primary' })
  })

  it('says why a champion cannot be hovered, but not that it is not our turn', () => {
    expect(setup({ availability: { allowed: false, reason: 'Not owned or disabled' } }).card.status(105))
      .toEqual({ text: 'Not owned or disabled', tone: 'muted' })
    expect(setup({ kind: null, availability: { allowed: false, reason: 'Not your turn — your pick is next' } }).card.status(105)).toBeNull()
  })

  it('arms on an ally\'s pick intent and hovers on the second click', () => {
    const { card, hover } = setup({ ally: true })
    card.click(105)
    expect(hover).not.toHaveBeenCalled()
    expect(card.status(105)).toEqual({ text: 'your Jungle\'s pick — click again', tone: 'warning' })
    card.click(105)
    expect(hover).toHaveBeenCalledWith(105)
    expect(card.armed.value).toBeNull()
  })

  it('disarms on its own', () => {
    const { card, hover } = setup({ ally: true })
    card.click(105)
    vi.advanceTimersByTime(ARMED_MS)
    expect(card.armed.value).toBeNull()
    card.click(105)
    expect(hover).not.toHaveBeenCalled()
  })
})
