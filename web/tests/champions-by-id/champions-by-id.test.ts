import { describe, expect, it } from 'vitest'
import * as vue from 'vue'
import type { ChampionStaticListItem } from '#shared/types/static-data'

/**
 * `useChampionsById` indexes the static champion list by id so rows can be
 * decorated without a linear scan each (#690).
 *
 * Same auto-import stand-in as `tests/visible-once/visible-once.test.ts`.
 */
Object.assign(globalThis, {
  computed: vue.computed,
  toValue: vue.toValue,
})

const { useChampionsById } = await import('#common/composables/useChampionsById')

const ahri: ChampionStaticListItem = { championId: 103, name: 'Ahri', iconUrl: '/ahri.png' }
const annie: ChampionStaticListItem = { championId: 1, name: 'Annie', iconUrl: '/annie.png' }

describe('useChampionsById', () => {
  it('maps every champion by its id', () => {
    const byId = useChampionsById([ahri, annie]).value
    expect(byId.size).toBe(2)
    expect(byId.get(103)).toBe(ahri)
    expect(byId.get(1)).toBe(annie)
    expect(byId.get(999)).toBeUndefined()
  })

  it('returns an empty map while the list is not loaded', () => {
    expect(useChampionsById(null).value.size).toBe(0)
    expect(useChampionsById(undefined).value.size).toBe(0)
  })

  it('keeps the last entry when an id repeats', () => {
    const renamed = { ...ahri, name: 'Ahri (new)' }
    expect(useChampionsById([ahri, renamed]).value.get(103)).toBe(renamed)
  })

  it('recomputes when the source list changes', () => {
    const champions = vue.ref<ChampionStaticListItem[] | null>(null)
    const byId = useChampionsById(champions)
    expect(byId.value.size).toBe(0)

    champions.value = [ahri]
    // A deep `ref` hands back reactive proxies, so compare by value here.
    expect(byId.value.get(103)).toStrictEqual(ahri)
  })
})
