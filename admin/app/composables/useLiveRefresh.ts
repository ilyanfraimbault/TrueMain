import type { Ref } from 'vue'
// Explicit imports rather than Nuxt/VueUse auto-imports: this composable is unit
// tested outside the Nuxt context (`tests/live-refresh`), where no auto-import
// exists.
import { useDocumentVisibility, useIntervalFn } from '@vueuse/core'
import { computed, ref, watch } from 'vue'

/**
 * Anything this composable knows how to re-run: a plain function, or a
 * `useFetch` result (only its `refresh` is used).
 */
export type LiveRefreshSource
  = | (() => unknown)
    | { refresh: () => unknown }

export interface LiveRefreshOptions {
  /** Milliseconds between two automatic refreshes while the tab is visible. */
  every?: number
}

export interface LiveRefresh {
  /** Epoch ms of the last completed automatic/manual refresh. */
  lastUpdatedAt: Ref<number>
  /** True while the operator has paused the timer (not while the tab is hidden). */
  paused: Ref<boolean>
  /** True while a refresh is in flight — the guard against overlapping fetches. */
  refreshing: Ref<boolean>
  /** Pause/resume the timer. Resuming re-fetches immediately. */
  toggle: () => void
  /** Refresh now and restart the countdown — what the manual refresh button calls. */
  refreshNow: () => Promise<void>
}

const DEFAULT_INTERVAL_MS = 30_000

/**
 * Keeps a panel's data current without a reload (#1411).
 *
 * Re-runs the given sources every `every` ms while the document is visible,
 * pauses when it is hidden — a background tab must not keep hammering the ops
 * API — and fires one immediate refresh when it comes back, since whatever it
 * shows is by then at least as stale as the time spent away.
 *
 * Refreshes never overlap: a tick that lands while the previous one is still in
 * flight is dropped rather than queued, so a slow endpoint degrades to "one
 * request at a time" instead of piling up.
 *
 * Deliberately given only the sources it should drive: on a paginated panel the
 * live set is the summary blocks, never the table the operator is reading (a
 * re-ordered page under the cursor is worse than a stale one).
 */
export function useLiveRefresh(
  sources: LiveRefreshSource | LiveRefreshSource[],
  options: LiveRefreshOptions = {},
): LiveRefresh {
  const every = options.every ?? DEFAULT_INTERVAL_MS
  const refreshers = (Array.isArray(sources) ? sources : [sources]).map(
    source => (typeof source === 'function' ? source : () => source.refresh()),
  )

  // Seeded at creation: the panel's own initial fetch is firing right now, so
  // "updated just now" is the honest reading rather than a blank.
  const lastUpdatedAt = ref(Date.now())
  const paused = ref(false)
  const refreshing = ref(false)

  async function run(): Promise<void> {
    if (refreshing.value) {
      return
    }
    refreshing.value = true
    try {
      await Promise.all(refreshers.map(refresh => refresh()))
    }
    finally {
      refreshing.value = false
      // Stamped even when a source rejected: the timestamp says when the panel
      // last tried, and a failed fetch surfaces through its own error state.
      lastUpdatedAt.value = Date.now()
    }
  }

  const visibility = useDocumentVisibility()
  const active = computed(() => !paused.value && visibility.value !== 'hidden')

  const { pause, resume } = useIntervalFn(run, every, { immediate: false })

  watch(active, (isActive, wasActive) => {
    if (!isActive) {
      pause()
      return
    }
    resume()
    // Only on a *return* to activity — not on the initial mount, where the
    // panel's own fetch has just run.
    if (wasActive === false) {
      void run()
    }
  }, { immediate: true })

  async function refreshNow(): Promise<void> {
    await run()
    // Restart the countdown from this refresh, so a manual click never leaves a
    // tick a few hundred ms behind it.
    if (active.value) {
      pause()
      resume()
    }
  }

  function toggle(): void {
    paused.value = !paused.value
  }

  return { lastUpdatedAt, paused, refreshing, toggle, refreshNow }
}
