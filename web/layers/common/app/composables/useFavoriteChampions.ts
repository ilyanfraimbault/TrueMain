import type { FavoriteChampion } from '#common/utils/favorites'
import { createFavoriteChampionsStore, FAVORITE_CHAMPIONS_STORAGE_KEY } from '#common/utils/favorites'
import { subscribeToStorageKey } from '#common/utils/favorites/storage'

/**
 * Starred champions (#2080), persisted in `localStorage` under their own key
 * next to the followed players.
 *
 * Same hydration contract as `useFavoriteTruemains` (read it there): an empty
 * list on the server and through the hydration render, the storage read in
 * `onMounted`, `hydrated` until then; and the same cross-tab sync.
 */
export function useFavoriteChampions() {
  const champions = useState<FavoriteChampion[]>('favorite-champions', () => [])
  const hydrated = useState<boolean>('favorite-champions:hydrated', () => false)

  const store = createFavoriteChampionsStore(champions)

  if (import.meta.client) {
    let release: (() => void) | null = null

    onMounted(() => {
      if (!hydrated.value) {
        store.loadChampionsFromStorage()
        hydrated.value = true
      }
      release = subscribeToStorageKey(FAVORITE_CHAMPIONS_STORAGE_KEY, () => store.loadChampionsFromStorage())
    })
    onBeforeUnmount(() => {
      release?.()
      release = null
    })
  }

  return {
    ...store,
    /** False until `localStorage` has been read — render a skeleton, not an empty state. */
    championsHydrated: readonly(hydrated),
  }
}
