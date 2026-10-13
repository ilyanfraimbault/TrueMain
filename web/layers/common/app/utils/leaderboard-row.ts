import type { LeaderboardPositions, LeaderboardStats, LeaderboardTopChampion } from '#shared/types/leaderboard'
import type { ChampionStaticListItem } from '#shared/types/static-data'
import { formatPercentage, getPositionIconUrl } from '#shared/utils/ddragon'
import { formatCount } from '#shared/utils/counts'
import { championDisplayName } from '#shared/utils/champion-name'
import { formatKdaRatio } from '#shared/utils/kda'
import { POSITION_BY_VALUE } from '#common/utils/positions'

// What a truemains leaderboard row shows beside the player, derived once for
// both rows that draw it: the site's `/truemains` table (`TruemainsTable`) and
// the champion page's truemains (`LeaderboardRow`) (#2028).

export interface LeaderboardLane {
  position: string
  iconUrl: string
  /** `Primary: Mid`, the icon's tooltip. */
  title: string
  primary: boolean
}

/** A champion's name, or the shared stand-in while the static list has not resolved it. */
export function leaderboardChampionName(championsById: ReadonlyMap<number, ChampionStaticListItem>, id: number): string {
  return championDisplayName(id, championsById.get(id))
}

/** A champion's icon URL; null until the static list resolves it. */
export function leaderboardChampionIcon(championsById: ReadonlyMap<number, ChampionStaticListItem>, id: number): string | null {
  return championsById.get(id)?.iconUrl ?? null
}

/**
 * The primary lane, then the secondary one when the player has it. Empty
 * without main-champion analysis, so the column stays empty rather than
 * guessing a lane.
 */
export function leaderboardLanes(positions: LeaderboardPositions | null): LeaderboardLane[] {
  if (!positions) return []
  return [positions.primary, positions.secondary]
    .filter((position): position is string => Boolean(position))
    .map((position, index) => ({
      position,
      iconUrl: getPositionIconUrl(position),
      title: `${index === 0 ? 'Primary' : 'Secondary'}: ${POSITION_BY_VALUE.get(position)?.label ?? position}`,
      primary: index === 0,
    }))
}

/**
 * A player is a one-trick pony of at most one champion (a play rate ≥ 85%
 * leaves no room for a second main), so any flagged top champion makes the
 * whole player one.
 */
export function isOneTrick(topChampions: readonly LeaderboardTopChampion[]): boolean {
  return topChampions.some(champion => champion.isOtp)
}

/** The sub-mains beside the signature champion. */
export const SUB_CHAMPION_SLOTS = 2

/**
 * The second and third top champions, always {@link SUB_CHAMPION_SLOTS} slots
 * long — padded with nulls, because a variable number of icons would shift
 * the columns beside them.
 */
export function subChampionSlots(topChampions: readonly LeaderboardTopChampion[]): (LeaderboardTopChampion | null)[] {
  return Array.from({ length: SUB_CHAMPION_SLOTS }, (_, index) => topChampions[index + 1] ?? null)
}

/** Stands in for a figure the aggregate cannot supply yet: a column never drops a cell. */
export const EMPTY_STAT = '—'

/** Games, KDA and win rate as the row writes them. */
export function leaderboardStatLabels(stats: LeaderboardStats): { games: string, kda: string, winRate: string } {
  return {
    games: formatCount(stats.games),
    kda: stats.kda === null ? EMPTY_STAT : formatKdaRatio(stats.kda),
    winRate: stats.winRate === null ? EMPTY_STAT : formatPercentage(stats.winRate, 0),
  }
}
