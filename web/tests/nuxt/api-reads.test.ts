import { flushPromises } from '@vue/test-utils'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { beforeEach, describe, expect, it, vi } from 'vitest'

// The champion and player reads each reach the API route their page needs, through the
// request-bound fetcher, with the query their filters set — and stay idle until the
// input that names the route is known.
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

async function read(run: () => unknown) {
  await mountSuspended(probe(run).component)
  await flushPromises()
}

beforeEach(() => {
  requestFetch.mockReset().mockResolvedValue(null)
})

describe('champion and player reads', () => {
  it('asks for a lane\'s duo partners with the filters it was given', async () => {
    await read(() => useChampionSynergies(266, 'TOP', { partnerPosition: 'JUNGLE', eloBracket: 'MASTER_PLUS', patch: '16.4' }))

    expect(requestFetch).toHaveBeenCalledWith('/champions/266/synergies', {
      baseURL: '/api',
      query: { position: 'TOP', partnerPosition: 'JUNGLE', eloBracket: 'MASTER_PLUS', patch: '16.4' },
      signal: expect.any(AbortSignal),
    })
  })

  it('waits for a lane before asking for duo partners', async () => {
    await read(() => useChampionSynergies(266, null))

    expect(requestFetch).not.toHaveBeenCalled()
  })

  it('asks for third picks once a partner is chosen, and not before', async () => {
    await read(() => useChampionTrioSynergies(266, 'BOTTOM', null, null))
    expect(requestFetch).not.toHaveBeenCalled()

    await read(() => useChampionTrioSynergies(266, 'BOTTOM', 412, 'UTILITY', { patch: '16.4' }))
    expect(requestFetch).toHaveBeenCalledWith(expect.stringContaining('/champions/266/'), expect.objectContaining({
      baseURL: '/api',
      query: expect.objectContaining({ position: 'BOTTOM', patch: '16.4' }),
    }))
  })

  it('compares an account with the champion\'s mains', async () => {
    await read(() => useChampionMainsComparison(266, ' Faker#KR1 ', { position: 'MIDDLE' }))

    expect(requestFetch).toHaveBeenCalledWith('/champions/266/mains-comparison', expect.objectContaining({
      baseURL: '/api',
      query: expect.objectContaining({ account: 'Faker#KR1', position: 'MIDDLE' }),
    }))
  })

  it('asks for nothing to compare without an account', async () => {
    await read(() => useChampionMainsComparison(266, '  '))

    expect(requestFetch).not.toHaveBeenCalled()
  })

  it('reads a player\'s activity and rank history by their Riot ID', async () => {
    await read(() => {
      useTruemainActivity('Faker-KR1')
      useTruemainRankHistory('Faker-KR1')
    })

    expect(requestFetch).toHaveBeenCalledWith('/truemains/Faker-KR1/activity', expect.objectContaining({ baseURL: '/api' }))
    expect(requestFetch).toHaveBeenCalledWith('/truemains/Faker-KR1/rank-history', expect.objectContaining({ baseURL: '/api' }))
  })
})
