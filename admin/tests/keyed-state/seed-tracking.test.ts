import type { SeedRequestReadModel } from '#site-shared/contract/api'
import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { SEED_POLL_INTERVAL_MS, SEED_POLL_TIMEOUT_MS, useSeedTracking } from '~/composables/useSeedTracking'
import { seedStatusRead, seedStepStates, trackedRequest } from '~/utils/seed-tracking'

// The single seed as one union, one poll loop per submit (#2471).

function request(over: Partial<SeedRequestReadModel> = {}): SeedRequestReadModel {
  return {
    id: 's1',
    gameName: 'Faker',
    tagLine: 'KR1',
    platformId: 'KR',
    status: 'Pending',
    error: null,
    requestedAtUtc: '2026-11-08T00:00:00Z',
    processedAtUtc: null,
    resolvedPuuid: null,
    resolvedRiotAccountId: null,
    ...over,
  }
}

describe('seed tracking states', () => {
  it('marks the resolve step as the one that failed, and leaves the last one to do', () => {
    expect(seedStepStates('Failed')).toEqual(['done', 'failed', 'todo'])
    expect(seedStepStates('Pending')).toEqual(['active', 'todo', 'todo'])
    expect(seedStepStates('Resolving')).toEqual(['done', 'active', 'todo'])
    expect(seedStepStates('Ingested')).toEqual(['done', 'done', 'done'])
  })

  it('settles on a terminal status and keeps polling otherwise', () => {
    expect(seedStatusRead(request({ status: 'Resolving' })).s).toBe('polling')
    expect(seedStatusRead(request({ status: 'Failed' })).s).toBe('settled')
    expect(trackedRequest({ s: 'timedOut', request: request() })?.id).toBe('s1')
    expect(trackedRequest({ s: 'rejected', error: 'x' })).toBeNull()
  })
})

describe('useSeedTracking', () => {
  const body = { gameName: 'Faker', tagLine: 'KR1', platformId: 'KR' }
  let getSeedRequest: ReturnType<typeof vi.fn>

  beforeEach(() => {
    vi.useFakeTimers()
    getSeedRequest = vi.fn().mockResolvedValue(request({ status: 'Resolving' }))
    vi.stubGlobal('getSeedRequest', getSeedRequest)
    // The API is idempotent: a pending Riot ID answers its existing id.
    vi.stubGlobal('seedAccount', vi.fn().mockResolvedValue({ id: 's1', status: 'Pending' }))
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  function mountTracking() {
    const hooks = { onSubmitted: vi.fn(), onSettled: vi.fn() }
    let tracking!: ReturnType<typeof useSeedTracking>
    const wrapper = mount(defineComponent({
      setup() {
        tracking = useSeedTracking(hooks)
        return () => h('div')
      },
    }))
    return { wrapper, tracking, hooks }
  }

  async function tick() {
    await vi.advanceTimersByTimeAsync(SEED_POLL_INTERVAL_MS)
    await flushPromises()
  }

  it('runs one poll loop when the same Riot ID is submitted again mid-poll', async () => {
    const { tracking } = mountTracking()
    await tracking.submit(body)
    expect(tracking.state.value.s).toBe('polling')

    // The second submit lands while the first loop's read is in flight.
    let release!: (value: SeedRequestReadModel) => void
    getSeedRequest.mockImplementationOnce(() => new Promise((resolve) => {
      release = resolve
    }))
    await vi.advanceTimersByTimeAsync(SEED_POLL_INTERVAL_MS)
    await tracking.submit(body)
    release(request({ status: 'Resolving' }))
    await flushPromises()

    getSeedRequest.mockClear()
    await tick()
    expect(getSeedRequest).toHaveBeenCalledTimes(1)
    await tick()
    expect(getSeedRequest).toHaveBeenCalledTimes(2)
  })

  it('stops polling once unmounted, even with a read in flight', async () => {
    const { wrapper, tracking } = mountTracking()
    await tracking.submit(body)
    let release!: (value: SeedRequestReadModel) => void
    getSeedRequest.mockImplementationOnce(() => new Promise((resolve) => {
      release = resolve
    }))
    await vi.advanceTimersByTimeAsync(SEED_POLL_INTERVAL_MS)
    wrapper.unmount()
    release(request({ status: 'Resolving' }))
    await flushPromises()

    getSeedRequest.mockClear()
    await vi.advanceTimersByTimeAsync(SEED_POLL_TIMEOUT_MS)
    expect(getSeedRequest).not.toHaveBeenCalled()
  })

  it('settles on a terminal read and tells the queue', async () => {
    const { tracking, hooks } = mountTracking()
    await tracking.submit(body)
    expect(hooks.onSubmitted).toHaveBeenCalledOnce()
    getSeedRequest.mockResolvedValue(request({ status: 'Failed', error: 'Unknown Riot ID' }))
    await tick()
    expect(tracking.state.value).toEqual({ s: 'settled', request: request({ status: 'Failed', error: 'Unknown Riot ID' }) })
    expect(hooks.onSettled).toHaveBeenCalledOnce()
  })

  it('times out with the last status it read', async () => {
    const { tracking } = mountTracking()
    await tracking.submit(body)
    await vi.advanceTimersByTimeAsync(SEED_POLL_TIMEOUT_MS + SEED_POLL_INTERVAL_MS)
    await flushPromises()
    expect(tracking.state.value.s).toBe('timedOut')
    expect(tracking.tracked.value?.status).toBe('Resolving')
  })

  it('reports a rejected submit and skips the loop for an already terminal request', async () => {
    const { tracking, hooks } = mountTracking()
    vi.stubGlobal('seedAccount', vi.fn().mockRejectedValue({ data: { detail: 'Bad Riot ID' } }))
    await tracking.submit(body)
    expect(tracking.state.value.s).toBe('rejected')

    vi.stubGlobal('seedAccount', vi.fn().mockResolvedValue({ id: 's1', status: 'Ingested' }))
    await tracking.submit(body)
    expect(tracking.state.value.s).toBe('settled')
    expect(hooks.onSettled).toHaveBeenCalledOnce()
    await tick()
    expect(getSeedRequest).not.toHaveBeenCalled()
  })
})
