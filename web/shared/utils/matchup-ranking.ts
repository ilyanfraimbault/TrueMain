import type { ChampionMatchupEntry } from '../types/champions'

type RankedMatchup = Pick<ChampionMatchupEntry, 'opponentChampionId' | 'winRateLowerBound' | 'winRateUpperBound'>

/** How many opponents each side of the champion page's matchup leaderboard lists. */
export const MATCHUP_LEADERBOARD_SIZE = 5

/**
 * The best and worst matchups of a champion's leaderboard (#1087).
 *
 * One function for the Matchups panel and the server-rendered matchup sentences
 * (#1954): the prose names the opponents the panel leads with, and two rankings
 * would be two definitions of "best matchup" on the same page.
 *
 * Best by the *lower* Wilson bound — "at worst, this matchup is this good".
 * Ranking the raw win rate instead is what put Sett-on-eleven-games (82%) above
 * Ambessa-on-739 (57%) at the top of every jungle champion: on a field of eighty
 * opponents the biggest rate is essentially always the smallest sample, so a raw
 * sort ranks variance, not matchups. The backend's games floor drops the noise;
 * this decides the order of what survives it.
 *
 * Worst by the *upper* bound ascending — "at best, this matchup is only this
 * good". Deliberately not the mirror of `best`: sorting the lower bound upwards
 * would put the thinnest samples at the bottom, which is the same bug pointing
 * down. Best's rows are excluded rather than clamped by index, since the two
 * sorts are different orders and could otherwise list the same opponent twice.
 *
 * A caller showing fewer rows slices these lists; it never re-ranks with a
 * smaller `limit`, which would shrink the exclusion set and let an opponent the
 * panel lists as a best matchup reappear among the worst.
 */
export function rankMatchups<T extends RankedMatchup>(
  entries: readonly T[],
  limit: number = MATCHUP_LEADERBOARD_SIZE,
): { best: T[], worst: T[] } {
  const best = [...entries]
    .sort((a, b) => b.winRateLowerBound - a.winRateLowerBound)
    .slice(0, limit)
  const bestIds = new Set(best.map(m => m.opponentChampionId))
  const worst = entries
    .filter(m => !bestIds.has(m.opponentChampionId))
    .sort((a, b) => a.winRateUpperBound - b.winRateUpperBound)
    .slice(0, limit)
  return { best, worst }
}
