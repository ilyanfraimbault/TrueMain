import type { ChampionPosition } from '#common/utils/positions'
import { isChampionPosition } from '#common/utils/positions'

/**
 * Starred champions and matchups (#2080): the pages a player comes back to,
 * next to the players they follow.
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
export const FAVORITE_PICKS_STORAGE_KEY = 'truemain:favorite-picks:v1'

/**
 * Cap per kind. Nothing here fans out into API calls (a row is drawn from the
 * static champion list), so the cap only bounds the page, at the player list's
 * size.
 */
export const FAVORITE_PICKS_LIMIT_PER_KIND = 30

export interface FavoriteChampion {
  kind: 'champion'
  championId: number
  /** The page's position when it was starred; null for a champion starred on every position. */
  position: ChampionPosition | null
  /** Epoch ms, newest first, and the eviction victim past the cap. */
  addedAt: number
}

export interface FavoriteMatchup {
  kind: 'matchup'
  championId: number
  /** The role opponent. */
  opponentChampionId: number
  /** A matchup is a position's: the page needs it to sample the lane. */
  position: ChampionPosition
  addedAt: number
}

export type FavoritePick = FavoriteChampion | FavoriteMatchup
export type FavoritePickKind = FavoritePick['kind']

/** What a call site hands over; `addedAt` is the store's. */
export type FavoritePickInput
  = | Omit<FavoriteChampion, 'addedAt'>
    | Omit<FavoriteMatchup, 'addedAt'>

function asChampionId(value: unknown): number | null {
  return typeof value === 'number' && Number.isInteger(value) && value > 0 ? value : null
}

/**
 * Identity: a champion is starred once per position, a matchup once per
 * (champion, opponent, position) — Ahri mid and Ahri top are two pages.
 */
export function favoritePickKey(pick: FavoritePickInput): string {
  return pick.kind === 'champion'
    ? `champion:${pick.championId}:${pick.position ?? ''}`
    : `matchup:${pick.championId}:${pick.opponentChampionId}:${pick.position}`
}

/**
 * Resolve an input or a stored record into a valid pick (without its time), or
 * `null`. The one notion of a valid entry, shared by the write and read paths
 * so the store never writes what it would discard on the next read (#872).
 */
export function resolveFavoritePick(value: unknown): FavoritePickInput | null {
  if (!value || typeof value !== 'object') return null
  const raw = value as Record<string, unknown>
  const championId = asChampionId(raw.championId)
  if (!championId) return null

  if (raw.kind === 'champion') {
    const position = isChampionPosition(raw.position) ? raw.position : null
    return { kind: 'champion', championId, position }
  }
  if (raw.kind === 'matchup') {
    const opponentChampionId = asChampionId(raw.opponentChampionId)
    if (!opponentChampionId || opponentChampionId === championId) return null
    if (!isChampionPosition(raw.position)) return null
    return { kind: 'matchup', championId, opponentChampionId, position: raw.position }
  }
  return null
}

function normalizeFavoritePick(value: unknown): FavoritePick | null {
  const pick = resolveFavoritePick(value)
  if (!pick) return null
  const addedAt = (value as Record<string, unknown>).addedAt
  return { ...pick, addedAt: typeof addedAt === 'number' && Number.isFinite(addedAt) ? addedAt : 0 } as FavoritePick
}

/**
 * Newest first, de-duplicated by identity (the most recent copy wins), capped
 * per kind. Every read and write goes through it, as `normalizeFavorites` does
 * for players.
 */
export function normalizeFavoritePicks(values: readonly unknown[]): FavoritePick[] {
  const byKey = new Map<string, FavoritePick>()
  for (const value of values) {
    const pick = normalizeFavoritePick(value)
    if (!pick) continue
    const key = favoritePickKey(pick)
    const existing = byKey.get(key)
    if (!existing || pick.addedAt > existing.addedAt) byKey.set(key, pick)
  }
  const picks = [...byKey.values()].sort((a, b) => b.addedAt - a.addedAt)
  const kept: Record<FavoritePickKind, number> = { champion: 0, matchup: 0 }
  return picks.filter(pick => ++kept[pick.kind] <= FAVORITE_PICKS_LIMIT_PER_KIND)
}

/** Parse a raw storage payload. Any corruption degrades to an empty list. */
export function parseStoredFavoritePicks(raw: string | null | undefined): FavoritePick[] {
  if (!raw) return []
  try {
    const parsed: unknown = JSON.parse(raw)
    return Array.isArray(parsed) ? normalizeFavoritePicks(parsed) : []
  }
  catch {
    return []
  }
}

/** Where a pick opens: the champion's page on its position, or the matchup page on the triple. */
export function favoritePickQuery(pick: FavoritePickInput): Record<string, string> {
  if (pick.kind === 'champion') return pick.position ? { position: pick.position } : {}
  return {
    champion: String(pick.championId),
    position: pick.position,
    opponent: String(pick.opponentChampionId),
  }
}
