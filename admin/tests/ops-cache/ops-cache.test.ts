import { describe, expect, it, vi } from 'vitest'
import { effectScope, nextTick, ref } from 'vue'
import {
  cleanQuery,
  clearOpsCache,
  createOpsCache,
  createOpsPrefetcher,
  hoveredRoute,
  OPS_CACHE_FRESH_MS,
  OPS_CACHE_TTL_MS,
  OPS_PREFETCH_SKIP_MS,
  oldestShownAt,
  opsCache,
  opsCacheKey,
  opsCacheShown,
} from '~/utils/ops-cache'
import { opsCacheFetchOptions, trackOpsCacheShown } from '~/utils/ops-cache-hooks'

// The navigation cache (#2306): what a panel draws when it mounts a key it fetched on
// an earlier visit, and what it must never draw — a payload past its TTL, one stored
// under another filter, or one from a session that ended.

function clock(start = 1_000_000) {
  let current = start
  return {
    now: () => current,
    advance: (ms: number) => {
      current += ms
    },
  }
}

describe('cleanQuery / opsCacheKey', () => {
  it('drops unset filters and keeps zeros and falses', () => {
    expect(cleanQuery({ a: undefined, b: null, c: '', d: 0, e: false, f: 'x' }))
      .toEqual({ d: 0, e: false, f: 'x' })
  })

  it('keys an unfiltered read by its path and a filtered one by path and query', () => {
    expect(opsCacheKey('/stats/overview')).toBe('ops:/stats/overview')
    expect(opsCacheKey('/riot-usage', { window: '24h' })).toBe('ops:/riot-usage:{"window":"24h"}')
  })
})

describe('createOpsCache', () => {
  it('serves an entry until its TTL, then forgets it', () => {
    const time = clock()
    const cache = createOpsCache(time.now)
    cache.set('k', { a: 1 })
    time.advance(OPS_CACHE_TTL_MS)
    expect(cache.get('k')?.value).toEqual({ a: 1 })
    time.advance(1)
    expect(cache.get('k')).toBeUndefined()
    expect(cache.size).toBe(0)
  })

  it('calls an entry fresh only for a few seconds', () => {
    const time = clock()
    const cache = createOpsCache(time.now)
    cache.set('k', 1)
    expect(cache.fresh('k')?.value).toBe(1)
    time.advance(OPS_CACHE_FRESH_MS)
    expect(cache.fresh('k')).toBeUndefined()
    expect(cache.get('k')?.value).toBe(1)
    expect(cache.fresh('missing')).toBeUndefined()
  })

  it('evicts the least recently written entry past its bound', () => {
    const cache = createOpsCache(Date.now, 2)
    cache.set('a', 1)
    cache.set('b', 2)
    cache.set('a', 3)
    cache.set('c', 4)
    expect(cache.get('b')).toBeUndefined()
    expect(cache.get('a')?.value).toBe(3)
    expect(cache.get('c')?.value).toBe(4)
  })

  it('empties on clear', () => {
    const cache = createOpsCache()
    cache.set('a', 1)
    cache.clear()
    expect(cache.get('a')).toBeUndefined()
  })
})

describe('clearOpsCache', () => {
  it('empties the portal cache and the on-screen registry together', () => {
    opsCache.set('ops:/x', 1)
    opsCacheShown.set('ops:/x', 1)
    clearOpsCache()
    expect(opsCache.size).toBe(0)
    expect(opsCacheShown.size).toBe(0)
  })
})

describe('oldestShownAt', () => {
  it('returns the oldest payload on screen, or null', () => {
    expect(oldestShownAt(new Map())).toBeNull()
    expect(oldestShownAt(new Map([['a', 30], ['b', 10], ['c', 20]]))).toBe(10)
  })
})

describe('hoveredRoute', () => {
  it('reads the route of the link under the target, without its query', () => {
    document.body.innerHTML = '<a href="/health?x=1"><span id="inner">Health</span></a><a href="?view=a"><i id="q"></i></a><p id="label">Monitor</p>'
    expect(hoveredRoute(document.getElementById('inner'))).toBe('/health')
    expect(hoveredRoute(document.getElementById('q'))).toBe('/')
    expect(hoveredRoute(document.getElementById('label'))).toBeNull()
    expect(hoveredRoute(null)).toBeNull()
  })
})

describe('createOpsPrefetcher', () => {
  it('warms each target of a route under the key its panel mounts', async () => {
    const cache = createOpsCache()
    const fetcher = vi.fn(async (url: string) => ({ url }))
    await createOpsPrefetcher(fetcher, cache)('/health')
    expect(fetcher).toHaveBeenCalledWith('/api/ops/pipeline-health', undefined)
    expect(fetcher).toHaveBeenCalledWith('/api/ops/riot-usage', { window: '24h' })
    expect(cache.get(opsCacheKey('/riot-usage', { window: '24h' }))?.value)
      .toEqual({ url: '/api/ops/riot-usage' })
  })

  it('does not warm a key fetched moments ago, nor one already in flight', async () => {
    const time = clock()
    const cache = createOpsCache(time.now)
    let release: (value: unknown) => void = () => {}
    const fetcher = vi.fn(() => new Promise((resolve) => {
      release = resolve
    }))
    const prefetch = createOpsPrefetcher(fetcher, cache, time.now)
    const first = prefetch('/database')
    void prefetch('/database')
    expect(fetcher).toHaveBeenCalledTimes(1)
    release([])
    await first
    await prefetch('/database')
    expect(fetcher).toHaveBeenCalledTimes(1)
    time.advance(OPS_PREFETCH_SKIP_MS)
    void prefetch('/database')
    expect(fetcher).toHaveBeenCalledTimes(2)
  })

  it('drops a failed prefetch and ignores routes without targets', async () => {
    const cache = createOpsCache()
    const fetcher = vi.fn(async () => {
      throw new Error('boom')
    })
    const prefetch = createOpsPrefetcher(fetcher, cache)
    await expect(prefetch('/configuration')).resolves.toBeUndefined()
    await prefetch('/patch-coverage')
    expect(fetcher).toHaveBeenCalledTimes(1)
    expect(cache.size).toBe(0)
  })
})

describe('opsCacheFetchOptions', () => {
  it('draws the stored payload as the default, for the live key', () => {
    const cache = createOpsCache()
    const key = ref('ops:/a')
    cache.set('ops:/a', 'A')
    const options = opsCacheFetchOptions(key, () => true, cache, new Map())
    expect(options.default()).toBe('A')
    key.value = 'ops:/b'
    expect(options.default()).toBeUndefined()
  })

  it('skips the request only for a fresh payload on the initial fetch', () => {
    const time = clock()
    const cache = createOpsCache(time.now)
    cache.set('k', 'v')
    const options = opsCacheFetchOptions('k', () => true, cache, new Map())
    expect(options.getCachedData('k', null, { cause: 'initial' })).toBe('v')
    expect(options.getCachedData('k', null, { cause: 'refresh:manual' })).toBeUndefined()
    time.advance(OPS_CACHE_FRESH_MS)
    expect(options.getCachedData('k', null, { cause: 'initial' })).toBeUndefined()
  })

  it('stores a success under the key the request was sent for, not the live one', () => {
    const cache = createOpsCache()
    const key = ref('ops:/w:{"window":"7d"}')
    const shown = new Map([['ops:/w:{"window":"7d"}', 1]])
    const options = opsCacheFetchOptions(key, () => true, cache, shown)
    const context = { response: { ok: true, _data: 'week' } }
    options.onRequest(context)
    key.value = 'ops:/w:{"window":"30d"}'
    options.onResponse(context)
    expect(cache.get('ops:/w:{"window":"7d"}')?.value).toBe('week')
    expect(cache.get('ops:/w:{"window":"30d"}')).toBeUndefined()
    expect(shown.size).toBe(0)
  })

  it('keeps nothing from a failed response, an unknown request, or after the session ended', () => {
    const cache = createOpsCache()
    let loggedIn = true
    const options = opsCacheFetchOptions('ops:/a', () => loggedIn, cache, new Map())
    const failed = { response: { ok: false, _data: 'err' } }
    options.onRequest(failed)
    options.onResponse(failed)
    options.onResponse({ response: { ok: true, _data: 'orphan' } })
    expect(cache.size).toBe(0)
    loggedIn = false
    const late = { response: { ok: true, _data: 'late' } }
    options.onRequest(late)
    options.onResponse(late)
    expect(cache.size).toBe(0)
  })

  it('stores by default when no session gate is given', () => {
    const cache = createOpsCache()
    const options = opsCacheFetchOptions('ops:/a', undefined, cache, new Map())
    const context = { response: { ok: true, _data: 1 } }
    options.onRequest(context)
    options.onResponse(context)
    expect(cache.get('ops:/a')?.value).toBe(1)
  })
})

describe('trackOpsCacheShown', () => {
  it('registers a stale payload drawn from the cache until the key moves or unmounts', async () => {
    const time = clock()
    const cache = createOpsCache(time.now)
    const shown = new Map<string, number>()
    cache.set('ops:/a', 1)
    const stampedAt = time.now()
    time.advance(OPS_CACHE_FRESH_MS + 1)
    const key = ref('ops:/a')
    const scope = effectScope()
    scope.run(() => trackOpsCacheShown(key, cache, shown))
    expect(shown.get('ops:/a')).toBe(stampedAt)

    key.value = 'ops:/b'
    await nextTick()
    expect(shown.size).toBe(0)

    key.value = 'ops:/a'
    await nextTick()
    expect(shown.has('ops:/a')).toBe(true)
    scope.stop()
    expect(shown.size).toBe(0)
  })

  it('does not register a payload fresh enough to have skipped the request', () => {
    const cache = createOpsCache()
    const shown = new Map<string, number>()
    cache.set('ops:/a', 1)
    const scope = effectScope()
    scope.run(() => trackOpsCacheShown('ops:/a', cache, shown))
    expect(shown.size).toBe(0)
    scope.stop()
  })
})
