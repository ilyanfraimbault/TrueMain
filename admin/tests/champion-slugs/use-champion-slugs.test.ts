import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import { useChampionSlugs } from '~~/app/composables/useChampionSlugs'

// The admin's champion slug map (#2291): the site's endpoint, kept in the shared
// cache so a return to /champions draws its links without a request.

interface FetchOptions {
  key: string
  getCachedData: (key: string, nuxtApp: unknown, ctx: { cause: string }) => unknown
  onResponse: (ctx: { response: { ok: boolean, _data: unknown } }) => void
}

describe('useChampionSlugs', () => {
  const store = new Map<string, { value: unknown }>()
  let url = ''
  let options: FetchOptions
  const data = ref<Record<string, string> | null>(null)

  beforeEach(() => {
    store.clear()
    vi.stubGlobal('opsCache', {
      get: (key: string) => store.get(key),
      set: (key: string, value: unknown) => store.set(key, { value }),
    })
    vi.stubGlobal('useFetch', (u: string, o: FetchOptions) => {
      url = u
      options = o
      return { data }
    })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('reads the site\'s slug map and exposes it', () => {
    const { slugs } = useChampionSlugs()
    expect(url).toBe('/api/static/champion-slugs')
    expect(slugs).toBe(data)
  })

  it('stores a successful answer and serves it on the next initial load', () => {
    useChampionSlugs()
    options.onResponse({ response: { ok: true, _data: { 103: 'ahri' } } })
    expect(options.getCachedData(options.key, null, { cause: 'initial' })).toEqual({ 103: 'ahri' })
    expect(options.getCachedData(options.key, null, { cause: 'refresh:manual' })).toBeUndefined()
  })

  it('never stores a failed answer', () => {
    useChampionSlugs()
    options.onResponse({ response: { ok: false, _data: { title: 'Unauthorized' } } })
    expect(options.getCachedData(options.key, null, { cause: 'initial' })).toBeUndefined()
  })
})
