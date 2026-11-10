import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref, toValue, watch } from 'vue'

/**
 * `usePlayerGameDetail` against stand-ins for the Nuxt auto-imports: TrueMain's copy of a
 * game first, read once; a game TrueMain has not ingested reads as not found outside the
 * shell. Each test loads the composable afresh, so its cache starts empty.
 */

const apiGet = vi.fn()

beforeEach(() => {
  vi.resetModules()
  apiGet.mockReset()
  vi.stubGlobal('usePlayerRecord', () => ({ record: computed(() => ({ platformId: 'EUW1' })) }))
  vi.stubGlobal('apiGet', apiGet)
  vi.stubGlobal('insideTauri', () => false)
  vi.stubGlobal('ref', ref)
  vi.stubGlobal('watch', watch)
  vi.stubGlobal('toValue', toValue)
})

async function detail(nameTag: string, matchId: string) {
  const { usePlayerGameDetail } = await import('~/composables/useSiteShims')
  const result = usePlayerGameDetail(nameTag, matchId)
  await vi.waitFor(() => expect(result.isLoading.value).toBe(false))
  return result
}

describe('usePlayerGameDetail', () => {
  it('reads TrueMain\'s copy of the game on the player\'s platform, in the background', async () => {
    apiGet.mockResolvedValue({ matchId: 'EUW1_42', participants: [] })

    const result = await detail('Synthetic-TAPE', '42')

    expect(apiGet).toHaveBeenCalledWith('/truemains/Synthetic-TAPE/matches/EUW1_42', {}, { background: true })
    expect(result.data.value?.matchId).toBe('EUW1_42')
    expect(result.notFound.value).toBe(false)
  })

  it('reads an ingested game once', async () => {
    apiGet.mockResolvedValue({ matchId: 'EUW1_42', participants: [] })
    const { usePlayerGameDetail } = await import('~/composables/useSiteShims')

    const first = usePlayerGameDetail('Synthetic-TAPE', '42')
    await vi.waitFor(() => expect(first.isLoading.value).toBe(false))
    const second = usePlayerGameDetail('Synthetic-TAPE', '42')
    await vi.waitFor(() => expect(second.isLoading.value).toBe(false))

    expect(apiGet).toHaveBeenCalledTimes(1)
    expect(second.data.value?.matchId).toBe('EUW1_42')
  })

  it('reports a game TrueMain does not have as not found outside the shell', async () => {
    apiGet.mockRejectedValue(new Error('404'))

    const result = await detail('Synthetic-TAPE', '43')

    expect(result.data.value).toBeNull()
    expect(result.notFound.value).toBe(true)
  })
})
