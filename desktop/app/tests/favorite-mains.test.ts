import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref, toValue, watch, type Ref } from 'vue'

/**
 * `useFavoriteMains` against stand-ins for the Nuxt auto-imports: the favorites who main
 * the champion, read from their profiles, each profile asked once.
 */

let states: Map<string, Ref<unknown>>
const apiGet = vi.fn()

beforeEach(() => {
  vi.resetModules()
  states = new Map()
  apiGet.mockReset()
  vi.stubGlobal('useState', (key: string, init: () => unknown) => {
    if (!states.has(key)) states.set(key, ref(init()))
    return states.get(key)
  })
  vi.stubGlobal('useFavoriteTruemains', () => ({
    favorites: ref([{ nameTag: 'Faker-KR1' }, { nameTag: 'Gone-EUW' }, { nameTag: 'Other-EUW' }]),
  }))
  vi.stubGlobal('apiGet', apiGet)
  vi.stubGlobal('watch', watch)
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('toValue', toValue)
})

describe('useFavoriteMains', () => {
  it('lists the favorites whose profile mains the champion, leaving out a profile that could not be read', async () => {
    apiGet.mockImplementation(async (path: string) => {
      if (path.includes('Gone-EUW')) throw new Error('404')
      return { mains: [{ championId: path.includes('Faker') ? 7 : 99 }] }
    })
    const { useFavoriteMains } = await import('~/composables/useFavoriteMains')

    const mains = useFavoriteMains(7)

    await vi.waitFor(() => expect(mains.value.map(entry => entry.favorite.nameTag)).toEqual(['Faker-KR1']))
    expect(apiGet).toHaveBeenCalledWith('/truemains/Faker-KR1/profile')
    expect(apiGet).toHaveBeenCalledTimes(3)
  })
})
