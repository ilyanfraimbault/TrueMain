import type { ChampionPosition } from '#common/utils/positions'
import { isChampionPosition } from '#common/utils/positions'

/**
 * Starred champions (#2080): the champion pages a player comes back to, next to
 * the players they follow.
 *
 * A key of their own rather than a second shape of `truemain:favorites:v1`.
 * That key's array of players is what every shipped build reads, and each of
 * them rewrites it whole on the next follow: a v2 that put champions in it would
 * be read by an older tab (or an older app build) as a list of unusable records,
 * dropped, then written back without them. A separate key needs no migration at
 * all — the player list keeps its shape and its readers, and a build that does
 * not know this key never touches it. Same rules otherwise: `localStorage`,
 * versioned, capped, every record untrusted on read.
 */
export const FAVORITE_CHAMPIONS_STORAGE_KEY = 'truemain:favorite-champions:v1'

/**
 * Cap on the list. Nothing here fans out into API calls (a row is drawn from
 * the static champion list), so the cap only bounds the page, at the player
 * list's size.
 */
export const FAVORITE_CHAMPIONS_LIMIT = 30

export interface FavoriteChampion {
  championId: number
  /** The page's position when it was starred; null for a champion starred on every position. */
  position: ChampionPosition | null
  /** Epoch ms, newest first, and the eviction victim past the cap. */
  addedAt: number
}

/** What a call site hands over; `addedAt` is the store's. */
export type FavoriteChampionInput = Omit<FavoriteChampion, 'addedAt'>

/** Identity: a champion is starred once per position — Ahri mid and Ahri top are two pages. */
export function favoriteChampionKey(champion: FavoriteChampionInput): string {
  return `${champion.championId}:${champion.position ?? ''}`
}

/**
 * Resolve an input or a stored record into a valid entry (without its time),
 * or `null`. The one notion of a valid entry, shared by the write and read
 * paths so the store never writes what it would discard on the next read (#872).
 */
export function resolveFavoriteChampion(value: unknown): FavoriteChampionInput | null {
  if (!value || typeof value !== 'object') return null
  const raw = value as Record<string, unknown>
  const championId = raw.championId
  if (typeof championId !== 'number' || !Number.isInteger(championId) || championId <= 0) return null
  return { championId, position: isChampionPosition(raw.position) ? raw.position : null }
}

function normalizeFavoriteChampion(value: unknown): FavoriteChampion | null {
  const champion = resolveFavoriteChampion(value)
  if (!champion) return null
  const addedAt = (value as Record<string, unknown>).addedAt
  return { ...champion, addedAt: typeof addedAt === 'number' && Number.isFinite(addedAt) ? addedAt : 0 }
}

/**
 * Newest first, de-duplicated by identity (the most recent copy wins), capped.
 * Every read and write goes through it, as `normalizeFavorites` does for players.
 */
export function normalizeFavoriteChampions(values: readonly unknown[]): FavoriteChampion[] {
  const byKey = new Map<string, FavoriteChampion>()
  for (const value of values) {
    const champion = normalizeFavoriteChampion(value)
    if (!champion) continue
    const key = favoriteChampionKey(champion)
    const existing = byKey.get(key)
    if (!existing || champion.addedAt > existing.addedAt) byKey.set(key, champion)
  }
  return [...byKey.values()]
    .sort((a, b) => b.addedAt - a.addedAt)
    .slice(0, FAVORITE_CHAMPIONS_LIMIT)
}

/** Parse a raw storage payload. Any corruption degrades to an empty list. */
export function parseStoredFavoriteChampions(raw: string | null | undefined): FavoriteChampion[] {
  if (!raw) return []
  try {
    const parsed: unknown = JSON.parse(raw)
    return Array.isArray(parsed) ? normalizeFavoriteChampions(parsed) : []
  }
  catch {
    return []
  }
}
