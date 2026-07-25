import type { FavoriteTruemain } from '~/utils/favorites'
import { createFavoritesStore, FAVORITES_STORAGE_KEY } from '~/utils/favorites'

/** Shared per-request state keys — one list for the whole app. */
const FAVORITES_STATE_KEY = 'favorite-truemains'
const FAVORITES_HYDRATED_STATE_KEY = 'favorite-truemains:hydrated'

/**
 * Followed truemains, persisted in `localStorage` (#531).
 *
 * ## Hydration contract — read before touching this
 *
 * `localStorage` does not exist on the server, so reading it during `setup()`
 * would make the client's first render disagree with the server HTML — the
 * exact class of bug PRs #838 / #840 had to undo. Instead:
 *
 *   1. `useState` seeds an **empty** list on the server. That empty list is
 *      what SSR renders and what travels in the Nuxt payload.
 *   2. On the client, the same empty list therefore backs the *hydration*
 *      render, which is byte-identical to the server HTML.
 *   3. Only in `onMounted` — after hydration has reconciled — do we read
 *      storage and fill the state. That is an ordinary reactive update, not
 *      part of hydration.
 *
 * `hydrated` exposes step 3 so callers can render a skeleton (never a "you
 * have no favorites" claim) while the real answer is still unknown.
 *
 * Steps 1-2 only hold for consumers that hydrate with the page. A consumer
 * rendered inside a *lazily* hydrated subtree (`hydrate-on-visible`) reconciles
 * long after `onMounted` filled the shared state, so it must gate on its own
 * per-instance mounted flag instead — see `FavoriteToggle`.
 */
/**
 * Cross-tab subscription, shared by every consumer.
 *
 * The state behind it is a single `useState` ref, so the subscription to it
 * should be single too: a leaderboard page mounts one `FavoriteToggle` per row,
 * and a per-instance listener would mean dozens of `window` handlers each
 * re-reading and re-parsing `localStorage` on every cross-tab write. One
 * refcounted registration does the same job once.
 *
 * Module-level mutable state is normally a cross-request hazard in Nuxt, but
 * this block is only ever reached from inside `import.meta.client`: SSR never
 * touches it, and the browser has exactly one app instance.
 */
let storageSubscriberCount = 0
let detachStorageListener: (() => void) | null = null

function subscribeToStorage(reload: () => void) {
  storageSubscriberCount++
  if (detachStorageListener) return

  // Another tab added or dropped a favorite (or cleared storage entirely,
  // which fires with `key === null`). Re-read so open tabs converge.
  //
  // `reload` closes over the shared `useState` ref rather than over the
  // component that happened to subscribe first, so it stays correct after that
  // component unmounts.
  const onStorageEvent = (event: StorageEvent) => {
    if (event.key !== null && event.key !== FAVORITES_STORAGE_KEY) return
    reload()
  }
  window.addEventListener('storage', onStorageEvent)
  detachStorageListener = () => window.removeEventListener('storage', onStorageEvent)
}

function unsubscribeFromStorage() {
  storageSubscriberCount--
  if (storageSubscriberCount > 0 || !detachStorageListener) return
  detachStorageListener()
  detachStorageListener = null
  storageSubscriberCount = 0
}

export function useFavoriteTruemains() {
  const favorites = useState<FavoriteTruemain[]>(FAVORITES_STATE_KEY, () => [])
  const hydrated = useState<boolean>(FAVORITES_HYDRATED_STATE_KEY, () => false)

  const store = createFavoritesStore(favorites)

  function syncFromStorage() {
    store.loadFromStorage()
    hydrated.value = true
  }

  if (import.meta.client) {
    // Tracked per instance so an unmount can only ever release a subscription
    // this instance actually took (a component created but never mounted must
    // not decrement the shared count).
    let subscribed = false

    onMounted(() => {
      // The read is idempotent, but the shared state only needs filling once
      // per client session — later mounts reuse it.
      if (!hydrated.value) syncFromStorage()
      subscribeToStorage(() => store.loadFromStorage())
      subscribed = true
    })
    onBeforeUnmount(() => {
      if (!subscribed) return
      subscribed = false
      unsubscribeFromStorage()
    })
  }

  return {
    ...store,
    /** False until `localStorage` has been read — render a skeleton, not an empty state. */
    hydrated: readonly(hydrated),
  }
}
