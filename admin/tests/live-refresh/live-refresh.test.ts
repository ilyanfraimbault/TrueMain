import { useLiveRefresh } from '~/composables/useLiveRefresh'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { effectScope, nextTick } from 'vue'

// `useLiveRefresh` is a timer plus a visibility rule, so both are driven here:
// fake timers for the interval, a stubbed `document.visibilityState` plus the
// `visibilitychange` event for the pause/resume half.

function setVisibility(state: 'visible' | 'hidden') {
  Object.defineProperty(document, 'visibilityState', {
    configurable: true,
    get: () => state,
  })
  document.dispatchEvent(new Event('visibilitychange'))
}

/** Let Vue's watchers (microtask-scheduled) and any resolved refresh settle. */
async function settle() {
  await nextTick()
  await Promise.resolve()
}

describe('useLiveRefresh', () => {
  let scope: ReturnType<typeof effectScope>

  beforeEach(() => {
    vi.useFakeTimers()
    setVisibility('visible')
    scope = effectScope()
  })

  afterEach(() => {
    scope.stop()
    vi.useRealTimers()
  })

  function run<T>(fn: () => T): T {
    return scope.run(fn) as T
  }

  it('re-runs the sources on every interval while the tab is visible', async () => {
    const refresh = vi.fn()
    run(() => useLiveRefresh(refresh, { every: 1000 }))
    await settle()

    // No fetch on creation: the panel's own initial request just ran.
    expect(refresh).toHaveBeenCalledTimes(0)

    await vi.advanceTimersByTimeAsync(1000)
    expect(refresh).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(2000)
    expect(refresh).toHaveBeenCalledTimes(3)
  })

  it('accepts a useFetch-shaped source and several sources at once', async () => {
    const fetchLike = { refresh: vi.fn() }
    const plain = vi.fn()
    run(() => useLiveRefresh([fetchLike, plain], { every: 1000 }))
    await settle()

    await vi.advanceTimersByTimeAsync(1000)
    expect(fetchLike.refresh).toHaveBeenCalledTimes(1)
    expect(plain).toHaveBeenCalledTimes(1)
  })

  it('never overlaps an in-flight refresh', async () => {
    // A refresh that never settles: every later tick must be dropped, not queued.
    const refresh = vi.fn(() => new Promise(() => {}))
    run(() => useLiveRefresh(refresh, { every: 1000 }))
    await settle()

    await vi.advanceTimersByTimeAsync(5000)
    expect(refresh).toHaveBeenCalledTimes(1)
  })

  it('stops while the document is hidden and refreshes once on return', async () => {
    const refresh = vi.fn()
    run(() => useLiveRefresh(refresh, { every: 1000 }))
    await settle()

    setVisibility('hidden')
    await settle()

    await vi.advanceTimersByTimeAsync(5000)
    expect(refresh).toHaveBeenCalledTimes(0)

    setVisibility('visible')
    await settle()
    // Immediately, without waiting for the next tick: what is on screen is at
    // least as stale as the time spent away.
    expect(refresh).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(1000)
    expect(refresh).toHaveBeenCalledTimes(2)
  })

  it('toggles the timer off and back on, re-fetching on resume', async () => {
    const refresh = vi.fn()
    const live = run(() => useLiveRefresh(refresh, { every: 1000 }))
    await settle()

    live.toggle()
    await settle()
    expect(live.paused.value).toBe(true)

    await vi.advanceTimersByTimeAsync(5000)
    expect(refresh).toHaveBeenCalledTimes(0)

    live.toggle()
    await settle()
    expect(live.paused.value).toBe(false)
    expect(refresh).toHaveBeenCalledTimes(1)
  })

  it('stamps lastUpdatedAt after each refresh', async () => {
    const refresh = vi.fn()
    const live = run(() => useLiveRefresh(refresh, { every: 1000 }))
    const initial = live.lastUpdatedAt.value

    await vi.advanceTimersByTimeAsync(1000)
    expect(live.lastUpdatedAt.value).toBeGreaterThan(initial)
  })

  it('refreshNow refreshes and restarts the countdown', async () => {
    const refresh = vi.fn()
    const live = run(() => useLiveRefresh(refresh, { every: 1000 }))
    await settle()

    await vi.advanceTimersByTimeAsync(800)
    await live.refreshNow()
    expect(refresh).toHaveBeenCalledTimes(1)

    // The remaining 200 ms of the original cycle are gone: the timer restarted.
    await vi.advanceTimersByTimeAsync(800)
    expect(refresh).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(200)
    expect(refresh).toHaveBeenCalledTimes(2)
  })
})
