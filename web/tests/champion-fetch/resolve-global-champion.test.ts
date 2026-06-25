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
  it('returns the slice and notEnoughData=false on success', async () => {
    const payload = champion('14.1')
    const fetcher = vi.fn().mockResolvedValue(payload)

    const outcome = await resolveGlobalChampion(fetcher, { patch: '14.1' })

    expect(outcome).toEqual({ data: payload, notEnoughData: false })
    expect(fetcher).toHaveBeenCalledTimes(1)
    expect(fetcher).toHaveBeenCalledWith({ patch: '14.1' })
  })

  it('flags notEnoughData (no throw) on a 404 with no filters', async () => {
    const fetcher = vi.fn().mockRejectedValue(fetchError(404))

    const outcome = await resolveGlobalChampion(fetcher, {})

    expect(outcome).toEqual({ data: null, notEnoughData: true })
    // No filters → no unfiltered fallback, just the one (failing) call.
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it('falls back to the unfiltered slice when a filtered slice 404s', async () => {
    const fallback = champion('14.2')
    const fetcher = vi
      .fn()
      .mockRejectedValueOnce(fetchError(404))
      .mockResolvedValueOnce(fallback)
    const onFallback = vi.fn()

    const outcome = await resolveGlobalChampion(fetcher, { position: 'MIDDLE' }, onFallback)

    expect(outcome).toEqual({ data: fallback, notEnoughData: false })
    expect(fetcher).toHaveBeenCalledTimes(2)
    expect(fetcher).toHaveBeenLastCalledWith() // unfiltered retry
    expect(onFallback).toHaveBeenCalledWith(fallback)
  })

  it('flags notEnoughData when both the filtered and unfiltered slices 404', async () => {
    const fetcher = vi
      .fn()
      .mockRejectedValueOnce(fetchError(404))
      .mockRejectedValueOnce(fetchError(404))
    const onFallback = vi.fn()

    const outcome = await resolveGlobalChampion(fetcher, { patch: '14.1', position: 'TOP' }, onFallback)

    expect(outcome).toEqual({ data: null, notEnoughData: true })
    expect(fetcher).toHaveBeenCalledTimes(2)
    expect(onFallback).not.toHaveBeenCalled()
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
