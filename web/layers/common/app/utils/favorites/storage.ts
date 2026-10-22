/**
 * `window.localStorage`, or `null` when unavailable — server-side rendering,
 * and browsers that throw on access (Safari private mode, blocked storage).
 * Every storage touch of the favorites stores goes through this so a blocked
 * browser degrades to an in-memory-only list instead of throwing on click.
 */
export function getStorage(): Storage | null {
  try {
    if (typeof window === 'undefined') return null
    return window.localStorage ?? null
  }
  catch {
    return null
  }
}

/** Write `value` under `key`, swallowing a full or disabled storage: the in-memory list keeps working. */
export function writeStorage(key: string, value: string) {
  const storage = getStorage()
  if (!storage) return
  try {
    storage.setItem(key, value)
  }
  catch {
    // Quota exceeded or storage disabled mid-session.
  }
}

/** Read `key`, or `undefined` when there is no storage to read (the caller keeps its state). */
export function readStorage(key: string): string | null | undefined {
  const storage = getStorage()
  if (!storage) return undefined
  try {
    return storage.getItem(key)
  }
  catch {
    return undefined
  }
}

/**
 * Cross-tab subscriptions, one `window` listener per storage key however many
 * consumers ask.
 *
 * Each favorites list is a single `useState` ref, so the subscription to it
 * should be single too: a leaderboard page mounts one `FavoriteToggle` per row,
 * and a per-instance listener would mean dozens of handlers each re-reading and
 * re-parsing `localStorage` on every cross-tab write. A refcounted registration
 * does the same job once.
 *
 * Module-level mutable state is normally a cross-request hazard in Nuxt, but
 * this is only ever reached from inside `import.meta.client`: SSR never
 * touches it, and the browser has exactly one app instance.
 */
const storageSubscriptions = new Map<string, { count: number, detach: () => void }>()

/**
 * Re-run `reload` whenever another tab writes `key` (or clears storage
 * entirely, which fires with `key === null`). `reload` must close over the
 * shared state, not over the component that subscribed first, so it stays
 * correct after that component unmounts. Returns the release.
 */
export function subscribeToStorageKey(key: string, reload: () => void): () => void {
  const existing = storageSubscriptions.get(key)
  if (existing) {
    existing.count++
  }
  else {
    const onStorageEvent = (event: StorageEvent) => {
      if (event.key !== null && event.key !== key) return
      reload()
    }
    window.addEventListener('storage', onStorageEvent)
    storageSubscriptions.set(key, {
      count: 1,
      detach: () => window.removeEventListener('storage', onStorageEvent),
    })
  }

  let released = false
  return () => {
    if (released) return
    released = true
    const subscription = storageSubscriptions.get(key)
    if (!subscription || --subscription.count > 0) return
    subscription.detach()
    storageSubscriptions.delete(key)
  }
}
