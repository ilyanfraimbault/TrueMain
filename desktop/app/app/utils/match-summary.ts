import type { ProfileRanked } from '#shared/types/profile'
import type { MatchSummaryResponse, MatchSummarySelf } from '#shared/types/matches'
import type { MatchDetailResponse } from '#shared/types/match-detail'
import type { GameParticipant, PlayerGame, RankedQueue, TimelineMark } from '~/types/record'
import type { ScoredParticipant } from '~/utils/match-performance'
import { gradeMatch } from '~/utils/match-performance'
import { killParticipation } from '~/utils/player-form'

/**
 * The player's record from their client, in the shapes the site's profile
 * components take — its ranked card, and the match summary its match row and
 * detail panel are built on.
 *
 * Only what the client measured is filled in. The performance score, the
 * placement and MVP/ACE (#2075) are TrueMain's own when it holds the game, else
 * graded here with the site's model from the client's scoreboard and timeline
 * marks (`utils/match-performance`). A game without its scoreboard, or a
 * remake, is not graded: zero, and never shown.
 */

/** The score half of a match row's `self`. */
export type GameScore = Pick<MatchSummarySelf, 'performanceScore' | 'placement' | 'isMvp' | 'isAce'>

/** A game this short was a remake (`lcu::record`): never graded. */
const REMAKE_SECONDS = 300

const UNGRADED: GameScore = { performanceScore: 0, placement: 0, isMvp: false, isAce: false }

/** TrueMain's score when it holds the game, else the client's game graded the site's way. */
export function gameScore(game: PlayerGame, stored?: GameScore | null): GameScore {
  if (stored?.placement) return stored
  if (game.remake || !game.participants.length) return UNGRADED
  const graded = gradeMatch(game.participants.map(scoredFromRecord), game.durationSeconds).get(game.participantId)
  return graded
    ? { performanceScore: graded.score, placement: graded.placement, isMvp: graded.isMvp, isAce: graded.isAce }
    : UNGRADED
}

function scoredFromRecord(p: GameParticipant): ScoredParticipant {
  return { ...p, teamPosition: p.position }
}

/**
 * The client's read of one game (`player_game`) with its ten players graded
 * — the scoreboard of the opened row, consistent with the row itself.
 */
export function gradeDetail(detail: MatchDetailResponse): MatchDetailResponse {
  if (detail.gameDurationSeconds < REMAKE_SECONDS) return detail
  const graded = gradeMatch(detail.participants.map(p => ({
    participantId: p.participantId,
    teamId: p.teamId,
    teamPosition: p.teamPosition,
    win: p.win,
    kills: p.kills,
    deaths: p.deaths,
    assists: p.assists,
    cs: p.cs,
    damageToChampions: p.totalDamageDealtToChampions,
    goldEarned: p.goldEarned,
    visionScore: p.visionScore,
    // The client's detail carries them (`lcu::detail`); the site's type does not know them.
    marks: (p as { marks?: TimelineMark[] }).marks ?? [],
  })), detail.gameDurationSeconds)
  return {
    ...detail,
    participants: detail.participants.map((p) => {
      const placement = graded.get(p.participantId)
      return placement
        ? { ...p, performanceScore: placement.score, placement: placement.placement, isMvp: placement.isMvp, isAce: placement.isAce }
        : p
    }),
  }
}

const MAP_MODES: Record<number, string> = { 11: 'CLASSIC', 12: 'ARAM', 30: 'CHERRY' }

export function toMatchSummary(game: PlayerGame, lpDelta: number | null, stored?: GameScore | null): MatchSummaryResponse {
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
      roleBoundItemId: game.roleBoundItem,
      teamId: game.teamId,
      position: game.position,
      win: game.win,
      lpDelta,
      ...gameScore(game, stored),
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
