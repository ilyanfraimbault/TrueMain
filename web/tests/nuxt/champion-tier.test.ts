import { flushPromises } from '@vue/test-utils'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { beforeEach, describe, expect, it, vi } from 'vitest'

// The champion banner's tier is the directory's own row for the slice (#1734), so the
// letter matches the tier list and the champion list under the same filters. These
// pin the query that makes it the same row, and that a missing lane or a failed read
// leaves the banner without a tier instead of raising.
const { requestFetch } = vi.hoisted(() => ({ requestFetch: vi.fn() }))
mockNuxtImport('useRequestFetch', () => () => requestFetch)

function probe<T>(run: () => T) {
  let result!: T
  const component = defineComponent({
    setup() {
      result = run()
      return () => h('div')
    },
  })
  return { component, result: () => result }
}

const slice = {
  championId: 103,
  position: 'MIDDLE',
  patch: '16.20',
  eloBracket: 'EMERALD_PLUS',
  truemainsOnly: false,
}

beforeEach(() => {
  requestFetch.mockReset()
})

describe('useChampionTier', () => {
  it('reads the directory row of the champion on the slice', async () => {
    requestFetch.mockResolvedValue({ rows: [{ tier: 'S' }] })
    const { component, result } = probe(() => useChampionTier(slice))

    await mountSuspended(component)
    await flushPromises()

    expect(requestFetch).toHaveBeenCalledWith('/champions/directory', {
      baseURL: '/api',
      query: {
        pageSize: 1,
        championId: 103,
        position: 'MIDDLE',
        patch: '16.20',
        eloBracket: 'EMERALD_PLUS',
        truemainsOnly: 'false',
      },
      signal: expect.any(AbortSignal),
    })
    expect(result().value).toBe('S')
  })

  it('leaves the truemains filter to the API default when it is on', async () => {
    requestFetch.mockResolvedValue({ rows: [] })
    const { component, result } = probe(() => useChampionTier({ ...slice, patch: null, eloBracket: null, truemainsOnly: true }))

    await mountSuspended(component)
    await flushPromises()

    expect(requestFetch.mock.calls[0]?.[1]?.query).toEqual({ pageSize: 1, championId: 103, position: 'MIDDLE' })
    expect(result().value).toBeNull()
  })

  it('asks nothing without a lane', async () => {
    const { component, result } = probe(() => useChampionTier({ ...slice, position: null }))

    await mountSuspended(component)
    await flushPromises()

    expect(requestFetch).not.toHaveBeenCalled()
    expect(result().value).toBeNull()
  })

  it('shows no tier when the read fails', async () => {
    requestFetch.mockRejectedValue(new Error('[GET] "/api/champions/directory": 503 Service Unavailable'))
    // Another champion: the slice of the first test is already in Nuxt's async data, keyed by its query.
    const { component, result } = probe(() => useChampionTier({ ...slice, championId: 157 }))

    await mountSuspended(component)
    await flushPromises()

    expect(result().value).toBeNull()
  })
})
