import type { GameTeam } from '~/types/game'
import type { KnownPlayer, LaningForm, LoadingView, PlayerForm, PositionGames, Read, RecentGame } from '~/types/loading'
import type { RankedQueue } from '~/types/record'
import type { TruemainMark } from '~/utils/player-intel'

/**
 * The loading screen for a browser-only `npm run dev`: the late-game
 * scenario's ten players, with forms written by hand — one still being read,
 * one the client could not read, one hidden, one autofilled, one on a
 * streak, one ranked in Flex only, one unranked. Dev only: `useLoadingPlayers`
 * imports it behind `import.meta.dev`, so no build carries it.
 */
export function devLoadingView(): LoadingView {
  const now = Date.now()
  // Results newest first as `W`/`L`, each game a few hours before the last.
  const recent = (championId: number, position: string | null, results: string): RecentGame[] =>
    [...results].map((result, index) => ({
      championId,
      position,
      win: result === 'W',
      kills: (index * 3 + 4) % 11,
      deaths: (index * 5 + 2) % 8,
      assists: (index * 7 + 3) % 13,
      playedAt: now - (index + 1) * 5 * 3_600_000,
    }))
  // The streak the latest games make, as the shell counts it.
  const streakOf = (latest: RecentGame[]) => {
    const first = latest[0]
    if (!first) return 0
    const run = latest.findIndex(game => game.win !== first.win)
    const games = run === -1 ? latest.length : run
    return first.win ? games : -games
  }
  const form = (
    games: number,
    championGames: number,
    championWins: number,
    latest: RecentGame[],
    positions: PositionGames[],
  ): PlayerForm => ({
    games,
    championGames,
    championWins,
    championKills: championGames * 6,
    championDeaths: championGames * 4,
    championAssists: championGames * 7,
    positions,
    streak: streakOf(latest),
    recent: latest,
  })
  const roles = (...entries: [string, number][]) => entries.map(([position, games]) => ({ position, games }))
  const rank = (tier: string, division: string, leaguePoints: number, queueType = 'RANKED_SOLO_5x5'): RankedQueue =>
    ({ queueType, tier, division, leaguePoints, wins: 48, losses: 41, isProvisional: false, provisionalGamesRemaining: 0 })
  // Games and wins on the champion, then the average leads at fifteen minutes.
  const laning = (games: number, wins: number, gold: number, cs: number, xp: number): LaningForm =>
    ({ games, wins, measured: games, goldDiff15: gold, csDiff15: cs, xpDiff15: xp })
  const ok = <T>(value: T): Read<T> => ({ state: 'ok', value })
  const pending = { state: 'pending' } as const
  const failed = { state: 'failed' } as const
  const known = (
    riotId: string,
    championId: number,
    team: GameTeam,
    position: string,
    reads: Pick<KnownPlayer, 'form' | 'rank' | 'laning'>,
    isMe = false,
  ): KnownPlayer => ({ kind: 'known', riotId, championId, team, position, isMe, ...reads })
  return {
    status: 'reading',
    players: [
      known('Synthetic#TAPE', 103, 'ORDER', 'MIDDLE', { form: ok(form(20, 14, 9, recent(103, 'MIDDLE', 'WWWLWLLWWL'), roles(['MIDDLE', 15], ['TOP', 2]))), rank: ok(rank('EMERALD', 'II', 63)), laning: ok(laning(10, 7, 640, 11, 380)) }, true),
      known('Wrenfield#FR1', 61, 'CHAOS', 'MIDDLE', { form: ok(form(20, 2, 0, recent(61, 'JUNGLE', 'LLLLWWLWLW'), roles(['JUNGLE', 14], ['UTILITY', 3], ['MIDDLE', 1]))), rank: ok(rank('PLATINUM', 'I', 12)), laning: ok(laning(2, 0, -900, -14, -520)) }),
      known('Harrowgate#0001', 266, 'ORDER', 'TOP', { form: ok(form(18, 11, 7, recent(266, 'TOP', 'WLWWLWLWWL'), roles(['TOP', 9], ['JUNGLE', 6]))), rank: ok(rank('DIAMOND', 'IV', 0)), laning: ok(laning(10, 6, 210, 4, 150)) }),
      known('Lumen#0001', 254, 'ORDER', 'JUNGLE', { form: ok(form(20, 6, 3, recent(254, 'JUNGLE', 'LWLWWLLWLW'), roles(['JUNGLE', 18]))), rank: ok(rank('GOLD', 'I', 88, 'RANKED_FLEX_SR')), laning: ok(laning(6, 3, -350, -3, -200)) }),
      known('Orrin#EUW', 222, 'ORDER', 'BOTTOM', { form: pending, rank: pending, laning: pending }),
      known('Nyrox#LOL', 412, 'ORDER', 'UTILITY', { form: ok(form(20, 17, 11, recent(412, 'UTILITY', 'WWWWWLLWLW'), roles(['UTILITY', 19]))), rank: ok(rank('MASTER', 'I', 214)), laning: ok(laning(10, 7, 120, 2, 60)) }),
      known('Solenne#0001', 54, 'CHAOS', 'TOP', { form: ok(form(4, 0, 0, recent(54, null, 'WWLW'), [])), rank: ok(null), laning: ok({ games: 0, wins: 0, measured: 0, goldDiff15: null, csDiff15: null, xpDiff15: null }) }),
      { kind: 'hidden', championId: 234, team: 'CHAOS', position: 'JUNGLE', isMe: false },
      known('Brambleheart#FR1', 81, 'CHAOS', 'BOTTOM', { form: failed, rank: failed, laning: failed }),
      known('Ashvale#LOL', 111, 'CHAOS', 'UTILITY', { form: ok(form(20, 8, 6, recent(111, 'UTILITY', 'LLLWLWWWLW'), roles(['UTILITY', 8], ['BOTTOM', 5]))), rank: ok(rank('EMERALD', 'IV', 37)), laning: ok(laning(8, 6, -150, -1, 90)) }),
    ],
    platformId: 'EUW1',
  }
}

/**
 * `GET /truemains/lookup`'s answer for the scenario above (#1910): two true
 * mains of the champion they are on, one of them a one-trick. Wrenfield is
 * listed too, on another champion, to show that such a player gets no mark.
 */
export function devTruemainMarks(): TruemainMark[] {
  const mark = (riotId: string, championId: number, championMatches: number, isOtp: boolean, dedication: number): TruemainMark =>
    ({ riotId, nameTag: riotId.replace('#', '-'), championId, championMatches, totalMatches: 50, playRate: championMatches / 50, isOtp, masteryPoints: null, dedication })
  return [
    mark('Nyrox#LOL', 412, 44, true, 91.4),
    mark('Ashvale#LOL', 111, 23, false, 64.2),
    mark('Wrenfield#FR1', 64, 30, false, 70.1),
  ]
}
