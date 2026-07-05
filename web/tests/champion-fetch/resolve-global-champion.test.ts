import { describe, expect, it, vi } from 'vitest'
import type { ChampionResponse } from '~~/shared/types/champions'
import { resolveGlobalChampion } from '~~/app/utils/champion-fetch'

/** Build an ofetch-style FetchError: a real Error carrying `statusCode`. */
function fetchError(statusCode: number): Error {
  return Object.assign(new Error(`[GET] "/api/champions/1": ${statusCode}`), { statusCode })
}

/** A minimal champion payload — only identity matters for these assertions. */
function champion(patch: string): ChampionResponse {
  return { patch } as unknown as ChampionResponse
}

describe('resolveGlobalChampion', () => {
  it('returns the slice with no fallback on success', async () => {
    const payload = champion('14.1')
    const fetcher = vi.fn().mockResolvedValue(payload)

    const outcome = await resolveGlobalChampion(fetcher, { patch: '14.1' })

    expect(outcome).toEqual({ data: payload, fallbackData: null })
    expect(fetcher).toHaveBeenCalledTimes(1)
    expect(fetcher).toHaveBeenCalledWith({ patch: '14.1' })
  })

  it('resolves to null data (no throw) on a 404 with no filters', async () => {
    const fetcher = vi.fn().mockRejectedValue(fetchError(404))

    const outcome = await resolveGlobalChampion(fetcher, {})

    expect(outcome).toEqual({ data: null, fallbackData: null })
    // No filters → no unfiltered fallback, just the one (failing) call.
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it('falls back to the default slice (patch/position dropped) when a filtered slice 404s', async () => {
    const fallback = champion('14.2')
    const fetcher = vi
      .fn()
      .mockRejectedValueOnce(fetchError(404))
      .mockResolvedValueOnce(fallback)

    const outcome = await resolveGlobalChampion(fetcher, { position: 'MIDDLE' })

    // data and fallbackData are the same slice — the caller stashes fallbackData.
    expect(outcome).toEqual({ data: fallback, fallbackData: fallback })
    expect(fetcher).toHaveBeenCalledTimes(2)
    // Retry drops patch/position; no rank was set, so nothing is preserved.
    expect(fetcher).toHaveBeenLastCalledWith({})
  })

  it('keeps the rank in the fallback when a pinned patch 404s', async () => {
    const fallback = champion('14.2')
    const fetcher = vi
      .fn()
      .mockRejectedValueOnce(fetchError(404))
      .mockResolvedValueOnce(fallback)

    const outcome = await resolveGlobalChampion(fetcher, { patch: '14.1', eloBracket: 'GOLD' })

    expect(outcome).toEqual({ data: fallback, fallbackData: fallback })
    expect(fetcher).toHaveBeenCalledTimes(2)
    // The explicit rank is preserved into the retry; only the patch is dropped.
    expect(fetcher).toHaveBeenLastCalledWith({ eloBracket: 'GOLD' })
  })

  it('does not fall back when only a rank is set (empty rank → no data)', async () => {
    const fetcher = vi.fn().mockRejectedValue(fetchError(404))

    const outcome = await resolveGlobalChampion(fetcher, { eloBracket: 'IRON' })

    // Nothing auto-resolvable to drop and we won't silently drop the rank, so we
    // conclude "no data for this rank" without a second call.
    expect(outcome).toEqual({ data: null, fallbackData: null })
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it('resolves to null when the rank-preserving fallback also 404s', async () => {
    const fetcher = vi
      .fn()
      .mockRejectedValueOnce(fetchError(404))
      .mockRejectedValueOnce(fetchError(404))

    const outcome = await resolveGlobalChampion(fetcher, { patch: '14.1', eloBracket: 'IRON' })

    expect(outcome).toEqual({ data: null, fallbackData: null })
    expect(fetcher).toHaveBeenCalledTimes(2)
    expect(fetcher).toHaveBeenLastCalledWith({ eloBracket: 'IRON' })
  })

  it('resolves to null data when both the filtered and unfiltered slices 404', async () => {
    const fetcher = vi
      .fn()
      .mockRejectedValueOnce(fetchError(404))
      .mockRejectedValueOnce(fetchError(404))

    const outcome = await resolveGlobalChampion(fetcher, { patch: '14.1', position: 'TOP' })

    expect(outcome).toEqual({ data: null, fallbackData: null })
    expect(fetcher).toHaveBeenCalledTimes(2)
  })

  it('propagates non-404 failures instead of swallowing them', async () => {
    for (const status of [429, 500, 503]) {
      const fetcher = vi.fn().mockRejectedValue(fetchError(status))
      await expect(resolveGlobalChampion(fetcher, { patch: '14.1' })).rejects.toMatchObject({ statusCode: status })
      // A real failure must not trigger the unfiltered fallback.
      expect(fetcher).toHaveBeenCalledTimes(1)
    }
  })

  it('propagates a network error (no status) instead of flagging no-data', async () => {
    const fetcher = vi.fn().mockRejectedValue(new Error('network down'))

    await expect(resolveGlobalChampion(fetcher, {})).rejects.toThrow('network down')
  })

  it('propagates a non-404 raised by the unfiltered fallback', async () => {
    const fetcher = vi
      .fn()
      .mockRejectedValueOnce(fetchError(404))
      .mockRejectedValueOnce(fetchError(500))

    await expect(
      resolveGlobalChampion(fetcher, { patch: '14.1' }),
    ).rejects.toMatchObject({ statusCode: 500 })
    expect(fetcher).toHaveBeenCalledTimes(2)
  })
})
