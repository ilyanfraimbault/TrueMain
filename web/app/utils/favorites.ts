import type { ComputedRef, Ref } from 'vue'
import type { RegionSlug } from '~~/shared/types/leaderboard'
import { computed } from 'vue'
import { REGION_SLUGS } from '~~/shared/types/leaderboard'

/**
 * Client-side "followed truemains" store (#531).
 *
 * There is no backend identity yet, so the list lives in `localStorage` and is
 * therefore *browser-scoped*, not account-scoped. Riot SSO sync is explicitly
 * deferred — when it lands, this module becomes the local cache in front of the
 * server list rather than the source of truth.
 *
 * Everything here is deliberately framework-free (a plain `Ref` in, a plain
 * object out) so it can be unit-tested without a Nuxt runtime; the Nuxt glue
 * — shared per-request state, mount-time hydration, cross-tab sync — lives in
 * `useFavoriteTruemains`.
 */

/** Storage key. Versioned so a future shape change can migrate instead of crash. */
export const FAVORITES_STORAGE_KEY = 'truemain:favorites:v1'

/**
 * Hard cap on the stored list. The favorites view fetches a profile plus a
 * short match feed per entry, so an unbounded list would fan out into an
 * unbounded number of API calls. Adding past the cap evicts the oldest entry.
 */
export const FAVORITES_LIMIT = 30

export interface FavoriteTruemain {
  /**
   * `{gameName}-{tagLine}` (or the bare game name when untagged) — the same
   * slug the profile route, the leaderboard rows and the sitemap use, so a
   * favorite always links to a real page.
   */
  nameTag: string
  gameName: string
  tagLine: string | null
  /** Region slug for the flag badge. Null when the platform has no pill. */
  region: RegionSlug | null
  /** DDragon profile icon id, so the card can draw an avatar before the profile fetch resolves. */
  profileIconId: number | null
  /** Epoch ms. Orders the list, newest first, and picks the eviction victim. */
  addedAt: number
}

/** What a call site hands over; `nameTag` and `addedAt` are derived. */
export interface FavoriteTruemainInput {
  gameName: string
  tagLine: string | null
  region?: RegionSlug | null
  profileIconId?: number | null
}

// Derived from the canonical list, never retyped — a slug missing here would
// silently null out a valid stored region.
const VALID_REGIONS = new Set<string>(REGION_SLUGS)

/**
 * App-wide profile slug for a Riot ID. Mirrors `LeaderboardRow` and the
 * sitemap builder: `-` is an unambiguous separator because Riot tag lines
 * never contain a hyphen.
 */
export function favoriteNameTag(gameName: string, tagLine: string | null | undefined): string {
  const name = gameName.trim()
  const tag = tagLine?.trim()
  return tag ? `${name}-${tag}` : name
}

/**
 * Identity key used for lookups and de-duplication. Riot IDs are matched
 * case-insensitively by the backend (and by the dev mock), so two casings of
 * the same player must never produce two entries.
 */
export function favoriteKey(nameTag: string): string {
  return nameTag.trim().toLowerCase()
}

function asString(value: unknown): string | null {
  return typeof value === 'string' && value.trim() ? value.trim() : null
}

/**
 * Coerce one persisted record back into a `FavoriteTruemain`, or `null` when
 * it is unusable. Storage is user-writable and survives deploys, so every
 * field is treated as untrusted input rather than assumed well-formed.
 */
export function normalizeFavorite(value: unknown): FavoriteTruemain | null {
  if (!value || typeof value !== 'object') return null
  const raw = value as Record<string, unknown>

  const gameName = asString(raw.gameName)
  if (!gameName) return null

  const tagLine = asString(raw.tagLine)
  const nameTag = asString(raw.nameTag) ?? favoriteNameTag(gameName, tagLine)

  const region = typeof raw.region === 'string' && VALID_REGIONS.has(raw.region)
    ? raw.region as RegionSlug
    : null

  const profileIconId = typeof raw.profileIconId === 'number' && Number.isFinite(raw.profileIconId)
    ? raw.profileIconId
    : null

  const addedAt = typeof raw.addedAt === 'number' && Number.isFinite(raw.addedAt)
    ? raw.addedAt
    : 0

  return { nameTag, gameName, tagLine, region, profileIconId, addedAt }
}

/**
 * Write-path counterpart of {@link normalizeFavorite}: resolve what a call site
 * hands over into the identity that would be stored, or `null` when the input is
 * not storable.
 *
 * The non-empty `gameName` requirement is the read path's, on purpose (#872).
 * Guarding on `nameTag` alone is *weaker* than it looks: an empty game name with
 * a populated tag line still yields a truncated `-1234`, which is non-empty and
 * would be written — then silently discarded by `normalizeFavorite` on the next
 * read. Two guards that disagree defeat the point of funnelling both paths
 * through one notion of a valid entry, so there is exactly one here too.
 */
export function resolveFavoriteIdentity(
  input: FavoriteTruemainInput,
): { gameName: string, tagLine: string | null, nameTag: string } | null {
  const gameName = asString(input.gameName)
  if (!gameName) return null
  const tagLine = asString(input.tagLine)
  return { gameName, tagLine, nameTag: favoriteNameTag(gameName, tagLine) }
}

/**
 * Newest first, de-duplicated by identity key, capped at {@link FAVORITES_LIMIT}.
 * The single choke point every read and write goes through, so the invariants
 * hold no matter which side (this tab, another tab, an old build) wrote the list.
 */
export function normalizeFavorites(values: readonly unknown[]): FavoriteTruemain[] {
  const byKey = new Map<string, FavoriteTruemain>()
  for (const value of values) {
    const entry = normalizeFavorite(value)
    if (!entry) continue
    const key = favoriteKey(entry.nameTag)
    const existing = byKey.get(key)
    // Duplicates collapse onto the most recent record, not onto whichever came
    // first: input order is not authoritative here (hand-edited storage, or a
    // list written by another build, can hold the stale copy first), so the
    // timestamps decide. `Map.set` on an existing key keeps the original slot,
    // which only matters for ties — the sort below settles the rest.
    if (!existing || entry.addedAt > existing.addedAt) byKey.set(key, entry)
  }
  const entries = [...byKey.values()]
  entries.sort((a, b) => b.addedAt - a.addedAt)
  return entries.slice(0, FAVORITES_LIMIT)
}

/** Parse a raw storage payload. Any corruption degrades to an empty list. */
export function parseStoredFavorites(raw: string | null | undefined): FavoriteTruemain[] {
  if (!raw) return []
  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  }
  catch {
    return []
  }
  return Array.isArray(parsed) ? normalizeFavorites(parsed) : []
}

export function serializeFavorites(favorites: readonly FavoriteTruemain[]): string {
  return JSON.stringify(favorites)
}

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
