import type { RegionSlug } from '#shared/types/leaderboard'
import { REGION_SLUGS } from '#shared/types/leaderboard'
import { truemainNameTag } from '#shared/utils/truemain-path'

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

/** App-wide profile slug for a Riot ID — the one `truemain-path` spells. */
export const favoriteNameTag = truemainNameTag

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
