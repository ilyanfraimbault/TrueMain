import type { Ref } from 'vue'
import type { FavoritePick, FavoritePickInput, FavoritePickKind } from './picks'
import {
  FAVORITE_PICKS_LIMIT_PER_KIND,
  FAVORITE_PICKS_STORAGE_KEY,
  favoritePickKey,
  normalizeFavoritePicks,
  parseStoredFavoritePicks,
  resolveFavoritePick,
} from './picks'
import { readStorage, writeStorage } from './storage'

// The stateful half of the champion / matchup favorites (#2080), on the player
// store's terms (`./store`): writes update the caller-owned ref first, then are
// mirrored to `localStorage`, last write wins across tabs.

export interface FavoritePicksStore {
  picks: Ref<FavoritePick[]>
  isFavoritePick: (input: FavoritePickInput) => boolean
  /** True once the input's kind is full — the toggle declines rather than evict. */
  isPickKindFull: (kind: FavoritePickKind) => boolean
  /** Flip the pick and return its new state (`true` = now starred); `false` for an unusable input. */
  togglePick: (input: FavoritePickInput) => boolean
  removePick: (input: FavoritePickInput) => void
  clearPicks: () => void
  loadPicksFromStorage: () => void
}

export function createFavoritePicksStore(
  state: Ref<FavoritePick[]>,
  now: () => number = () => Date.now(),
): FavoritePicksStore {
  function commit(next: FavoritePick[]) {
    state.value = normalizeFavoritePicks(next)
    writeStorage(FAVORITE_PICKS_STORAGE_KEY, JSON.stringify(state.value))
  }

  function isFavoritePick(input: FavoritePickInput): boolean {
    const key = favoritePickKey(input)
    return state.value.some(pick => favoritePickKey(pick) === key)
  }

  function isPickKindFull(kind: FavoritePickKind): boolean {
    return state.value.filter(pick => pick.kind === kind).length >= FAVORITE_PICKS_LIMIT_PER_KIND
  }

  function removePick(input: FavoritePickInput) {
    const key = favoritePickKey(input)
    commit(state.value.filter(pick => favoritePickKey(pick) !== key))
  }

  function togglePick(input: FavoritePickInput): boolean {
    const pick = resolveFavoritePick(input)
    if (!pick) return false
    if (isFavoritePick(pick)) {
      removePick(pick)
      return false
    }
    // Prepended so a shared timestamp still sorts it first.
    commit([{ ...pick, addedAt: now() } as FavoritePick, ...state.value])
    return true
  }

  function clearPicks() {
    commit([])
  }

  function loadPicksFromStorage() {
    const raw = readStorage(FAVORITE_PICKS_STORAGE_KEY)
    if (raw === undefined) return
    state.value = parseStoredFavoritePicks(raw)
  }

  return { picks: state, isFavoritePick, isPickKindFull, togglePick, removePick, clearPicks, loadPicksFromStorage }
}
