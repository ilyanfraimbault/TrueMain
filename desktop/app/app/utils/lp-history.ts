import type { RankHistoryEntry } from '~~/shared/types/rank-history'
import type { PlayerGame, RankedQueue } from '~/types/record'

/**
 * The player's Solo/Duo standing over time, as this app saw it.
 *
 * The client reports the standing *now* and keeps no history, so the app
 * writes one down each time it reads the record — only when the standing moved
 * — and keeps it on this machine. It starts empty on a fresh install and grows
 * with every game played while the app runs: the ranked card's LP curve, and
 * each game's LP gain or loss, are measured from it and never estimated.
 */

const SOLO_QUEUE = 420
const KEEP_DAYS = 90
const KEEP_ENTRIES = 400
const DAY_MS = 86_400_000

const storageKey = (riotId: string) => `truemain:rank-history:${riotId}`

export function readRankHistory(riotId: string): RankHistoryEntry[] {
  try {
    const raw = localStorage.getItem(storageKey(riotId))
    const parsed = raw ? JSON.parse(raw) : []
    return Array.isArray(parsed) ? parsed : []
  }
  catch {
    return []
  }
}

/** The standing in the site's snapshot shape; apex tiers carry division `I`, as on the site. */
export function toSnapshot(queue: RankedQueue, at: Date): RankHistoryEntry | null {
  if (!queue.tier || queue.tier === 'NONE') return null
  const division = queue.division && queue.division !== 'NA' ? queue.division : 'I'
  return { capturedAtUtc: at.toISOString(), tier: queue.tier, division, leaguePoints: queue.leaguePoints }
}

/** Append the current standing when it moved, drop what is past the window, and return the history. */
export function recordRankSnapshot(riotId: string, solo: RankedQueue | null, now = new Date()): RankHistoryEntry[] {
  const history = readRankHistory(riotId)
  const snapshot = solo ? toSnapshot(solo, now) : null
  const last = history.at(-1)
  const moved = snapshot && (!last
    || last.tier !== snapshot.tier
    || last.division !== snapshot.division
    || last.leaguePoints !== snapshot.leaguePoints)
  if (moved) history.push(snapshot)

  const cutoff = now.getTime() - KEEP_DAYS * DAY_MS
  const kept = history.filter(entry => new Date(entry.capturedAtUtc).getTime() >= cutoff).slice(-KEEP_ENTRIES)
  try {
    localStorage.setItem(storageKey(riotId), JSON.stringify(kept))
  }
  catch {
    // A private or full store: the curve simply does not grow this launch.
  }
  return kept
}

/**
 * What one Solo/Duo game did to the player's LP: the last standing seen before
 * it started against the last one seen after it ended and before the next
 * Solo/Duo game began. Null whenever that cannot be measured — no snapshot on
 * one side, or a promotion or demotion in between, the same rule the site's
 * match rows follow.
 */
export function lpDeltaOf(game: PlayerGame, games: PlayerGame[], history: RankHistoryEntry[]): number | null {
  if (game.queueId !== SOLO_QUEUE || game.remake) return null
  const start = game.playedAt
  const end = start + game.durationSeconds * 1000
  const solo = games.filter(other => other.queueId === SOLO_QUEUE && other.gameId !== game.gameId)
  const nextStart = solo
    .filter(other => other.playedAt > start)
    .reduce((earliest, other) => Math.min(earliest, other.playedAt), Number.POSITIVE_INFINITY)
  // The standing before must postdate the previous Solo/Duo game, or it would
  // fold that game's LP into this one.
  const previousEnd = solo
    .filter(other => other.playedAt < start)
    .reduce((latest, other) => Math.max(latest, other.playedAt + other.durationSeconds * 1000), Number.NEGATIVE_INFINITY)

  const at = (entry: RankHistoryEntry) => new Date(entry.capturedAtUtc).getTime()
  const before = history.filter(entry => at(entry) <= start && at(entry) >= previousEnd).at(-1)
  const after = history.filter(entry => at(entry) >= end && at(entry) < nextStart).at(-1)
  if (!before || !after || before.tier !== after.tier || before.division !== after.division) return null
  return after.leaguePoints - before.leaguePoints
}
