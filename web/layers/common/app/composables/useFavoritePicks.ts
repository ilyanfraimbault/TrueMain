import type { FavoritePick } from '#common/utils/favorites'
import { createFavoritePicksStore, FAVORITE_PICKS_STORAGE_KEY } from '#common/utils/favorites'
import { subscribeToStorageKey } from '#common/utils/favorites/storage'

/**
 * Starred champions and matchups (#2080), persisted in `localStorage` under
 * their own key next to the followed players.
 *
 * Same hydration contract as `useFavoriteTruemains` (read it there): an empty
 * list on the server and through the hydration render, the storage read in
 * `onMounted`, `hydrated` until then; and the same cross-tab sync.
 */
export function useFavoritePicks() {
  const picks = useState<FavoritePick[]>('favorite-picks', () => [])
  const hydrated = useState<boolean>('favorite-picks:hydrated', () => false)

  const store = createFavoritePicksStore(picks)

  if (import.meta.client) {
    let release: (() => void) | null = null

    onMounted(() => {
      if (!hydrated.value) {
        store.loadPicksFromStorage()
        hydrated.value = true
      }
      release = subscribeToStorageKey(FAVORITE_PICKS_STORAGE_KEY, () => store.loadPicksFromStorage())
    })
    onBeforeUnmount(() => {
      release?.()
      release = null
    })
  }

  return {
    ...store,
    /** False until `localStorage` has been read — render a skeleton, not an empty state. */
    picksHydrated: readonly(hydrated),
  }
}
