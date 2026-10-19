import type { ComputedRef, Ref } from 'vue'
import type { FavoriteTruemain, FavoriteTruemainInput } from './entries'
import { computed } from 'vue'
import {
  FAVORITES_LIMIT,
  FAVORITES_STORAGE_KEY,
  favoriteKey,
  normalizeFavorites,
  parseStoredFavorites,
  resolveFavoriteIdentity,
  serializeFavorites,
} from './entries'

// The stateful half of the favorites store: binds the pure entry rules of
// `./entries` to a caller-owned ref and mirrors every write to `localStorage`.

/**
 * `window.localStorage`, or `null` when unavailable — server-side rendering,
 * and browsers that throw on access (Safari private mode, blocked storage).
 * Every storage touch goes through this so a blocked browser degrades to an
 * in-memory-only list instead of throwing on click.
 */
function getStorage(): Storage | null {
  try {
    if (typeof window === 'undefined') return null
    return window.localStorage ?? null
  }
  catch {
    return null
  }
}

export interface FavoritesStore {
  favorites: Ref<FavoriteTruemain[]>
  count: ComputedRef<number>
  /** True once the list is full — further additions evict the oldest entry. */
  atLimit: ComputedRef<boolean>
  isFavorite: (nameTag: string) => boolean
  /**
   * Append a favorite. Past the cap this evicts the oldest — it does not refuse.
   * It *does* refuse an input {@link resolveFavoriteIdentity} rejects.
   */
  add: (input: FavoriteTruemainInput) => void
  remove: (nameTag: string) => void
  /** Flip the entry and return its new state (`true` = now followed). */
  toggle: (input: FavoriteTruemainInput) => boolean
  clear: () => void
  /** Re-read `localStorage` into the shared state. No-op on the server. */
  loadFromStorage: () => void
}

/**
 * Bind the favorites operations to a caller-owned state ref. Writes update the
 * ref first (so the UI reacts synchronously) and are then mirrored to
 * `localStorage`.
 *
 * `now` is injectable purely so tests can assert ordering and eviction without
 * sleeping.
 */
export function createFavoritesStore(
  state: Ref<FavoriteTruemain[]>,
  now: () => number = () => Date.now(),
): FavoritesStore {
  const count = computed(() => state.value.length)
  const atLimit = computed(() => state.value.length >= FAVORITES_LIMIT)

  /**
   * Mirror the whole list to storage. Deliberately last-write-wins, not a
   * merge.
   *
   * The concrete consequence: `storage` events do not fire in the tab that
   * wrote, so two tabs open at once can hold divergent state, and whichever
   * follows a stale read with a write drops what the other one added. It takes
   * two tabs mutating the list between each other's events to lose an entry,
   * and the cost is one re-click.
   *
   * Merging instead would mean a read-modify-write against `localStorage`,
   * which offers no atomicity between tabs anyway — so it would trade a rare
   * lost entry for a new class of interleaving bugs (resurrecting an entry the
   * user just removed in the other tab, and a cap that two writers can push
   * past). Not worth it for a browser-local bookmark list; the eventual Riot
   * SSO sync makes the server the arbiter and retires the question.
   */
  function persist() {
    const storage = getStorage()
    if (!storage) return
    try {
      storage.setItem(FAVORITES_STORAGE_KEY, serializeFavorites(state.value))
    }
    catch {
      // Quota exceeded or storage disabled mid-session: keep the in-memory
      // list working rather than breaking the click that triggered the write.
    }
  }

  function commit(next: FavoriteTruemain[]) {
    state.value = normalizeFavorites(next)
    persist()
  }

  function isFavorite(nameTag: string): boolean {
    const key = favoriteKey(nameTag)
    return state.value.some(entry => favoriteKey(entry.nameTag) === key)
  }

  /**
   * Append a favorite. **Eviction-based, not refusing**: adding past
   * {@link FAVORITES_LIMIT} drops the oldest entry rather than rejecting the
   * new one.
   *
   * That is deliberate. The cap is enforced in exactly one place —
   * `normalizeFavorites` — so reads and writes obey the same rule: a list that
   * arrives over-long from storage is truncated the same way an over-long write
   * is. Refusing here would introduce a *second*, different policy (refuse on
   * write, truncate on read) for the same invariant.
   *
   * `atLimit` exists so the UI can decline the click before it gets here (see
   * `FavoriteToggle`), which is a courtesy — a user's own click should never
   * silently drop someone they followed earlier — not a store-level guarantee.
   *
   * What it *does* refuse is an unusable identity, on the read path's terms —
   * see {@link resolveFavoriteIdentity}.
   */
  function add(input: FavoriteTruemainInput) {
    const identity = resolveFavoriteIdentity(input)
    if (!identity || isFavorite(identity.nameTag)) return
    const entry: FavoriteTruemain = {
      ...identity,
      region: input.region ?? null,
      profileIconId: input.profileIconId ?? null,
      addedAt: now(),
    }
    // Prepended so `normalizeFavorites`' newest-first ordering keeps it on top
    // even if two entries share a timestamp.
    commit([entry, ...state.value])
  }

  function remove(nameTag: string) {
    const key = favoriteKey(nameTag)
    commit(state.value.filter(entry => favoriteKey(entry.nameTag) !== key))
  }

  function toggle(input: FavoriteTruemainInput): boolean {
    // Resolved through the same helper as `add`, so an input that `add` refuses
    // reports "not followed" instead of claiming a follow that never landed.
    const identity = resolveFavoriteIdentity(input)
    if (!identity) return false
    if (isFavorite(identity.nameTag)) {
      remove(identity.nameTag)
      return false
    }
    add(input)
    return true
  }

  function clear() {
    commit([])
  }

  function loadFromStorage() {
    const storage = getStorage()
    if (!storage) return
    let raw: string | null = null
    try {
      raw = storage.getItem(FAVORITES_STORAGE_KEY)
    }
    catch {
      return
    }
    state.value = parseStoredFavorites(raw)
  }

  return { favorites: state, count, atLimit, isFavorite, add, remove, toggle, clear, loadFromStorage }
}
