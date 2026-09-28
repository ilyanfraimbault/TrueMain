import { flushPromises } from '@vue/test-utils'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { beforeEach, describe, expect, it, vi } from 'vitest'

// A client-side navigation awaits the destination's API data in setup, under the
// loading bar, so the page opens on its data rather than its skeleton (#1689).
// These pin the two halves the pages rely on: a composable's `ready` settles only
// once its fetch has, and a page that awaits it renders the data on its very first
// render. `mountSuspended` mounts outside hydration — exactly a client-side
// navigation's situation.
const { requestFetch } = vi.hoisted(() => ({ requestFetch: vi.fn() }))
mockNuxtImport('useRequestFetch', () => () => requestFetch)

beforeEach(() => {
  requestFetch.mockReset()
})

/** A page stand-in: awaits `ready` in setup, and records what its first render saw. */
function awaitingPage(run: () => { ready: Promise<unknown>, loaded: () => boolean }) {
  const firstRender: boolean[] = []
  const component = defineComponent({
    async setup() {
      const { ready, loaded } = run()
      await ready
      return () => {
        firstRender.push(loaded())
        return h('div', loaded() ? 'content' : 'skeleton')
      }
    },
  })
  return { component, firstRender }
}

describe('awaited page data on a client-side navigation', () => {
  it('useTruemainFetch fetches during setup and renders its data on the first render', async () => {
    const request = vi.fn(async () => ({ ok: true }))
    const { component, firstRender } = awaitingPage(() => {
      const data = ref<{ ok: boolean } | null>(null)
      const { ready, isInitialLoading } = useTruemainFetch<{ ok: boolean }>('Sheiden-1234', {
        request,
        validate: (response): response is { ok: boolean } => Boolean(response),
        onResponse: (response) => { data.value = response },
        onClear: () => { data.value = null },
      })
      return { ready, loaded: () => !isInitialLoading.value && data.value !== null }
    })

    const wrapper = await mountSuspended(component)
    await flushPromises()

    expect(firstRender).toEqual([true])
    expect(wrapper.text()).toBe('content')
    // The setup-time run replaces the mount-time one — never both.
    expect(request).toHaveBeenCalledTimes(1)
  })

  it('useChampion is no longer lazy: `ready` waits for the aggregate', async () => {
    let resolve!: (value: unknown) => void
    requestFetch.mockReturnValue(new Promise((r) => { resolve = r }))
    let settled = false
    const { component, firstRender } = awaitingPage(() => {
      const { filters } = useChampionFilters()
      const { data, ready } = useChampion(266, filters)
      return { ready: ready.then(() => { settled = true }), loaded: () => data.value != null }
    })

    const mounting = mountSuspended(component)
    await flushPromises()
    expect(settled).toBe(false)

    resolve({ championId: 266 })
    await mounting
    await flushPromises()

    expect(settled).toBe(true)
    expect(firstRender).toEqual([true])
  })
})
