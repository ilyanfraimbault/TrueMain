import { flushPromises } from '@vue/test-utils'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { beforeEach, describe, expect, it, vi } from 'vitest'

// Win rate by game duration (#537): held until the champion's lane resolves,
// then read on the page's lane, patch and rank.
const { requestFetch } = vi.hoisted(() => ({ requestFetch: vi.fn() }))
mockNuxtImport('useRequestFetch', () => () => requestFetch)

beforeEach(() => {
  requestFetch.mockReset()
  requestFetch.mockResolvedValue({ championId: 266, position: 'TOP', patch: '16.22', buckets: [], scalingIndex: null })
})

async function mountScaling(position: Ref<string | null>, enabled: Ref<boolean>) {
  let state!: ReturnType<typeof useChampionScaling>
  await mountSuspended(defineComponent({
    setup() {
      state = useChampionScaling(266, position, '16.22', enabled, 'MASTER_PLUS')
      return () => h('div')
    },
  }))
  return state
}

describe('useChampionScaling', () => {
  it('asks nothing while the lane is unknown, and answers an empty model', async () => {
    const state = await mountScaling(ref(null), ref(true))
    await flushPromises()

    expect(requestFetch).not.toHaveBeenCalled()
    expect(state.data.value?.buckets).toEqual([])
  })

  it('reads the lane, patch and rank once the gate opens', async () => {
    const enabled = ref(false)
    const state = await mountScaling(ref('TOP'), enabled)
    await flushPromises()
    expect(requestFetch).not.toHaveBeenCalled()

    enabled.value = true
    await flushPromises()

    expect(requestFetch).toHaveBeenCalledTimes(1)
    expect(requestFetch).toHaveBeenLastCalledWith('/champions/266/scaling', expect.objectContaining({
      query: { position: 'TOP', patch: '16.22', eloBracket: 'MASTER_PLUS' },
    }))
    expect(state.pending.value).toBe(false)
  })
})
