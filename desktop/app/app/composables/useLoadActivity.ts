/**
 * Whether the window is waiting on anything a page shows (#1788): a route
 * change, a shared page's first read, a read of TrueMain. One count for the
 * whole window, drawn by `AppLoadingBar` across its top edge — the app has no
 * header to hang the site's bar under, and a read through the shell can take
 * seconds, long enough for a window showing nothing to read as frozen.
 */
const inFlight = ref(0)

/** Count `work` as loading until it settles; the promise is handed back as it is. */
export function trackLoad<T>(work: Promise<T>): Promise<T> {
  inFlight.value++
  return work.finally(() => {
    inFlight.value--
  })
}

/**
 * A load with no promise of its own — a navigation, a suspended page — counted
 * from `begin` until `end`. Either may be called again: a second `begin` ends
 * the previous span first, and an `end` without one is a no-op, so a guard
 * that skips `begin` can never leave the bar running.
 */
export function loadSpan() {
  let settle: (() => void) | null = null
  return {
    begin() {
      settle?.()
      void trackLoad(new Promise<void>(resolve => (settle = resolve)))
    },
    end() {
      settle?.()
      settle = null
    },
  }
}

export function useLoadActivity() {
  return { loading: computed(() => inFlight.value > 0) }
}
