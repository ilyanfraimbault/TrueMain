import type { ComputedRef, Ref } from 'vue'
import type { FavoriteChampion, FavoriteChampionInput } from './champions'
import { computed } from 'vue'
import {
  FAVORITE_CHAMPIONS_LIMIT,
  FAVORITE_CHAMPIONS_STORAGE_KEY,
  favoriteChampionKey,
  normalizeFavoriteChampions,
  parseStoredFavoriteChampions,
  resolveFavoriteChampion,
} from './champions'
import { readStorage, writeStorage } from './storage'

// The stateful half of the starred champions (#2080), on the player store's
// terms (`./store`): writes update the caller-owned ref first, then are
// mirrored to `localStorage`, last write wins across tabs.

export interface FavoriteChampionsStore {
  champions: Ref<FavoriteChampion[]>
  /** True once the list is full — the toggle declines rather than evict. */
  championsFull: ComputedRef<boolean>
  isFavoriteChampion: (input: FavoriteChampionInput) => boolean
  /** Flip the entry and return its new state (`true` = now starred); `false` for an unusable input. */
  toggleChampion: (input: FavoriteChampionInput) => boolean
  clearChampions: () => void
  loadChampionsFromStorage: () => void
}

export function createFavoriteChampionsStore(
  state: Ref<FavoriteChampion[]>,
  now: () => number = () => Date.now(),
): FavoriteChampionsStore {
  const championsFull = computed(() => state.value.length >= FAVORITE_CHAMPIONS_LIMIT)

  function commit(next: FavoriteChampion[]) {
    state.value = normalizeFavoriteChampions(next)
    writeStorage(FAVORITE_CHAMPIONS_STORAGE_KEY, JSON.stringify(state.value))
  }

  function isFavoriteChampion(input: FavoriteChampionInput): boolean {
    const key = favoriteChampionKey(input)
    return state.value.some(entry => favoriteChampionKey(entry) === key)
  }

  function toggleChampion(input: FavoriteChampionInput): boolean {
    const champion = resolveFavoriteChampion(input)
    if (!champion) return false
    const key = favoriteChampionKey(champion)
    if (isFavoriteChampion(champion)) {
      commit(state.value.filter(entry => favoriteChampionKey(entry) !== key))
      return false
    }
    // Prepended so a shared timestamp still sorts it first.
    commit([{ ...champion, addedAt: now() }, ...state.value])
    return true
  }

  function clearChampions() {
    commit([])
  }

  function loadChampionsFromStorage() {
    const raw = readStorage(FAVORITE_CHAMPIONS_STORAGE_KEY)
    if (raw === undefined) return
    state.value = parseStoredFavoriteChampions(raw)
  }

  return { champions: state, championsFull, isFavoriteChampion, toggleChampion, clearChampions, loadChampionsFromStorage }
}
