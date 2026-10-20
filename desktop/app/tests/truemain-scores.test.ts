import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, nextTick, ref, toValue, watch, type Ref } from 'vue'

/**
 * `useTruemainScores` against stand-ins for the Nuxt auto-imports: the suite
 * has no Nuxt runtime. Each test loads the composable afresh.
 */

let states: Map<string, Ref<unknown>>
let platformId: Ref<string | null>
const apiGet = vi.fn()

beforeEach(() => {
  vi.resetModules()
  states = new Map()
  platformId = ref('EUW1')
  apiGet.mockReset()
  vi.stubGlobal('useState', (key: string, init: () => unknown) => {
    if (!states.has(key)) states.set(key, ref(init()))
    return states.get(key)
  })
  vi.stubGlobal('usePlayerRecord', () => ({ record: computed(() => ({ platformId: platformId.value })) }))
  vi.stubGlobal('apiGet', apiGet)
  vi.stubGlobal('watch', watch)
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('toValue', toValue)
})

const self = (performanceScore: number, placement: number, isMvp = false) =>
  ({ performanceScore, placement, isMvp, isAce: false, championId: 1 })

async function scores(nameTag: string) {
  const { useTruemainScores } = await import('~/composables/useTruemainScores')
  const result = useTruemainScores(nameTag)
  await vi.waitFor(() => expect(apiGet).toHaveBeenCalled())
  await nextTick()
  return result
}

describe('useTruemainScores', () => {
  it('keys TrueMain\'s scores by the client\'s game id, on the player\'s platform only', async () => {
    apiGet.mockResolvedValue({ matches: [
      { matchId: 'EUW1_7212000020', self: self(71, 1, true) },
      { matchId: 'NA1_99', self: self(40, 8) },
    ] })
    const result = await scores('Synthetic-TAPE')
    await vi.waitFor(() => expect(Object.keys(result.value)).toEqual(['7212000020']))
    expect(result.value['7212000020']).toEqual({ performanceScore: 71, placement: 1, isMvp: true, isAce: false })
    expect(apiGet).toHaveBeenCalledWith('/truemains/Synthetic-TAPE/matches', { pageSize: 50 }, { background: true })
  })

  it('asks once per Riot ID and session', async () => {
    apiGet.mockResolvedValue({ matches: [] })
    await scores('Synthetic-TAPE')
    const { useTruemainScores } = await import('~/composables/useTruemainScores')
    useTruemainScores('Synthetic-TAPE')
    await nextTick()
    expect(apiGet).toHaveBeenCalledTimes(1)
  })

  it('leaves every game to the client for a player TrueMain does not track', async () => {
    apiGet.mockRejectedValue(new Error('404'))
    const result = await scores('Nobody-EUW')
    expect(result.value).toEqual({})
  })

  it('waits for the platform before asking', async () => {
    platformId.value = null
    const { useTruemainScores } = await import('~/composables/useTruemainScores')
    useTruemainScores('Synthetic-TAPE')
    await nextTick()
    expect(apiGet).not.toHaveBeenCalled()
    apiGet.mockResolvedValue({ matches: [] })
    platformId.value = 'EUW1'
    await vi.waitFor(() => expect(apiGet).toHaveBeenCalledTimes(1))
  })
})
