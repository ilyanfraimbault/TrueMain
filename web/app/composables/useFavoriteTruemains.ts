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
export function useFavoriteTruemains() {
  const favorites = useState<FavoriteTruemain[]>(FAVORITES_STATE_KEY, () => [])
  const hydrated = useState<boolean>(FAVORITES_HYDRATED_STATE_KEY, () => false)

  const store = createFavoritesStore(favorites)

  function syncFromStorage() {
    store.loadFromStorage()
    hydrated.value = true
  }

  // Another tab added or dropped a favorite (or cleared storage entirely,
  // which fires with `key === null`). Re-read so open tabs converge.
  function onStorageEvent(event: StorageEvent) {
    if (event.key !== null && event.key !== FAVORITES_STORAGE_KEY) return
    store.loadFromStorage()
  }

  if (import.meta.client) {
    onMounted(() => {
      // The read is idempotent, but the shared state only needs filling once
      // per client session — later mounts reuse it.
      if (!hydrated.value) syncFromStorage()
      window.addEventListener('storage', onStorageEvent)
    })
    onBeforeUnmount(() => {
      window.removeEventListener('storage', onStorageEvent)
    })
  }

  return {
    ...store,
    /** False until `localStorage` has been read — render a skeleton, not an empty state. */
    hydrated: readonly(hydrated),
  }
}
