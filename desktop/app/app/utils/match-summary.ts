import type { ProfileRanked } from '#shared/types/profile'
import type { MatchSummaryResponse } from '#shared/types/matches'
import type { PlayerGame, RankedQueue } from '~/types/record'
import { killParticipation } from '~/utils/player-form'

/**
 * The player's record from their client, in the shapes the site's profile
 * components take — its ranked card, and the match summary its match row and
 * detail panel are built on.
 *
 * Only what the client measured is filled in. The site's per-match performance
 * score does not exist for these games (TrueMain never scored them), so it is
 * zero here and never shown.
 */

const MAP_MODES: Record<number, string> = { 11: 'CLASSIC', 12: 'ARAM', 30: 'CHERRY' }

export function toMatchSummary(game: PlayerGame, lpDelta: number | null): MatchSummaryResponse {
  return {
    matchId: String(game.gameId),
    queueId: game.queueId,
    gameMode: MAP_MODES[game.mapId] ?? 'CLASSIC',
    gameStartTimeUtc: new Date(game.playedAt).toISOString(),
    gameDurationSeconds: game.durationSeconds,
    self: {
      championId: game.championId,
      championLevel: game.championLevel,
      summoner1Id: game.spells[0],
      summoner2Id: game.spells[1],
      primaryStyleId: game.primaryStyle,
      subStyleId: game.subStyle,
      keystoneId: game.keystone,
      kills: game.kills,
      deaths: game.deaths,
      assists: game.assists,
      cs: game.cs,
      killParticipation: killParticipation(game) ?? 0,
      items: game.items,
      trinketItemId: game.trinket,
      roleBoundItemId: 0,
      teamId: game.teamId,
      position: game.position,
      win: game.win,
      lpDelta,
      performanceScore: 0,
      placement: 0,
      isMvp: false,
      isAce: false,
    },
    participants: game.participants,
  }
}

/** The client's Solo/Duo standing as the site's ranked card reads it. */
export function toProfileRanked(queue: RankedQueue | null): ProfileRanked | null {
  if (!queue?.tier || queue.tier === 'NONE') return null
  const games = queue.wins + queue.losses
  return {
    tier: queue.tier,
    division: queue.division === 'NA' ? 'I' : queue.division,
    leaguePoints: queue.leaguePoints,
    wins: queue.wins,
    losses: queue.losses,
    winRate: games ? queue.wins / games : null,
  }
}
