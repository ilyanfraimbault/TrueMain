import { flushPromises } from '@vue/test-utils'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { championTrendKey } from '#common/composables/useChampionTrend'

// The champion banner's moves and the trend chart read `/champions/{id}/trend`;
// they must describe the page's rank and population, not every rank (#2353).
const { requestFetch } = vi.hoisted(() => ({ requestFetch: vi.fn() }))
mockNuxtImport('useRequestFetch', () => () => requestFetch)

beforeEach(() => {
  requestFetch.mockReset()
  requestFetch.mockResolvedValue({ championId: 266, position: 'TOP', points: [] })
})

function mountTrend(eloBracket: Ref<string | undefined>, truemainsOnly: Ref<boolean>) {
  return mountSuspended(defineComponent({
    setup() {
      useChampionTrend(266, 'TOP', true, { eloBracket, truemainsOnly })
      return () => h('div')
    },
  }))
}

describe('useChampionTrend slice', () => {
  it('keys each rank filter and population apart', () => {
    const keys = new Set([
      championTrendKey(266, 'TOP', undefined, false),
      championTrendKey(266, 'TOP', 'MASTER_PLUS', false),
      championTrendKey(266, 'TOP', 'DIAMOND_PLUS', false),
      championTrendKey(266, 'TOP', 'MASTER_PLUS', true),
    ])
    expect(keys.size).toBe(4)
  })

  it('sends the page\'s rank and population, and asks again when the rank changes', async () => {
    const eloBracket = ref<string | undefined>('MASTER_PLUS')
    const truemainsOnly = ref(false)
    await mountTrend(eloBracket, truemainsOnly)
    await flushPromises()

    expect(requestFetch).toHaveBeenLastCalledWith('/champions/266/trend', expect.objectContaining({
      query: { position: 'TOP', eloBracket: 'MASTER_PLUS', truemainsOnly: 'false' },
    }))

    eloBracket.value = 'DIAMOND_PLUS'
    await flushPromises()
    expect(requestFetch).toHaveBeenCalledTimes(2)
    expect(requestFetch).toHaveBeenLastCalledWith('/champions/266/trend', expect.objectContaining({
      query: { position: 'TOP', eloBracket: 'DIAMOND_PLUS', truemainsOnly: 'false' },
    }))
  })

  it('leaves the API defaults implicit: mains only, no rank filter', async () => {
    await mountTrend(ref(undefined), ref(true))
    await flushPromises()

    expect(requestFetch).toHaveBeenLastCalledWith('/champions/266/trend', expect.objectContaining({
      query: { position: 'TOP' },
    }))
  })
})
