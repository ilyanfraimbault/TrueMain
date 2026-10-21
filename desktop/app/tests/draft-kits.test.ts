import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, nextTick, ref, watch } from 'vue'
import type { Kit } from '#shared/types/kit'
import { useDraftKits } from '../app/composables/useDraftKits'
import { kit } from './kit-fixtures'

// The kits in champion select (#2205) against a stand-in store: offered on our
// lane, none picked at first, the one picked kept for the game.
const active = ref<Record<string, string>>({})
const setActive = vi.fn(async (championId: number, position: string, id: string | null) => {
  const key = `${championId}:${position}`
  const others = Object.entries(active.value).filter(([other]) => other !== key)
  active.value = Object.fromEntries(id ? [...others, [key, id]] : others)
})
const all: Kit[] = [kit(), kit({ id: 'k2', positions: ['TOP'] })]

beforeEach(() => {
  active.value = { '103:MIDDLE': 'k1' }
  setActive.mockClear()
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('ref', ref)
  vi.stubGlobal('watch', watch)
  vi.stubGlobal('useKitAction', () => async (action: () => Promise<unknown>) => {
    await action()
    return true
  })
  vi.stubGlobal('useKits', () => ({
    kitsFor: (championId: number, position: string) => all.filter(k => k.championId === championId && k.positions.includes(position)),
    activeIdFor: (championId: number, position: string) => active.value[`${championId}:${position}`] ?? null,
    setActive,
  }))
})

afterEach(() => vi.unstubAllGlobals())

describe('useDraftKits', () => {
  it('offers the lane\'s kits, starts on the draft\'s build, and keeps the kit picked for the game', async () => {
    const pick = ref<{ championId: number, position: string } | null>({ championId: 103, position: 'MIDDLE' })
    const { kits, kitId, pickKit } = useDraftKits(pick)
    expect(kits.value.map(k => k.id)).toEqual(['k1'])
    expect(kitId.value).toBeNull()
    // A kit left from an earlier champion select is not the default.
    expect(active.value).toEqual({})

    pickKit('k1')
    await nextTick()
    expect(active.value).toEqual({ '103:MIDDLE': 'k1' })

    // Another champion's build read, then ours again: the kit stays picked.
    pick.value = null
    await nextTick()
    pick.value = { championId: 103, position: 'MIDDLE' }
    await nextTick()
    expect(kitId.value).toBe('k1')
    expect(kits.value).toHaveLength(1)

    pickKit(null)
    await nextTick()
    expect(active.value).toEqual({})

    pickKit('k1')
    pick.value = { championId: 103, position: 'TOP' }
    await nextTick()
    expect(kits.value.map(k => k.id)).toEqual(['k2'])
    expect(kitId.value).toBeNull()
  })
})
