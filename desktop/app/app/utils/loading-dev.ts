import type { LoadingView, PlayerForm, PositionGames, RecentGame } from '~/types/loading'
import type { RankedQueue } from '~/types/record'

/**
 * The loading screen for a browser-only `npm run dev`: the late-game
 * scenario's ten players, with forms written by hand — one still being read,
 * one the client could not read, one anonymous, one autofilled, one on a
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
  return {
    players: [
      { riotId: 'Synthetic#TAPE', championId: 103, team: 'ORDER', position: 'MIDDLE', isMe: true, anonymous: false, form: form(20, 14, 9, recent(103, 'MIDDLE', 'WWWLWLLWWL'), roles(['MIDDLE', 15], ['TOP', 2])), failed: false, rank: rank('EMERALD', 'II', 63), rankRead: true, rankFailed: false },
      { riotId: 'Wrenfield#FR1', championId: 61, team: 'CHAOS', position: 'MIDDLE', isMe: false, anonymous: false, form: form(20, 2, 0, recent(61, 'JUNGLE', 'LLLLWWLWLW'), roles(['JUNGLE', 14], ['UTILITY', 3], ['MIDDLE', 1])), failed: false, rank: rank('PLATINUM', 'I', 12), rankRead: true, rankFailed: false },
      { riotId: 'Harrowgate#0001', championId: 266, team: 'ORDER', position: 'TOP', isMe: false, anonymous: false, form: form(18, 11, 7, recent(266, 'TOP', 'WLWWLWLWWL'), roles(['TOP', 9], ['JUNGLE', 6])), failed: false, rank: rank('DIAMOND', 'IV', 0), rankRead: true, rankFailed: false },
      { riotId: 'Lumen#0001', championId: 254, team: 'ORDER', position: 'JUNGLE', isMe: false, anonymous: false, form: form(20, 6, 3, recent(254, 'JUNGLE', 'LWLWWLLWLW'), roles(['JUNGLE', 18])), failed: false, rank: rank('GOLD', 'I', 88, 'RANKED_FLEX_SR'), rankRead: true, rankFailed: false },
      { riotId: 'Orrin#EUW', championId: 222, team: 'ORDER', position: 'BOTTOM', isMe: false, anonymous: false, form: null, failed: false, rank: null, rankRead: false, rankFailed: false },
      { riotId: 'Nyrox#LOL', championId: 412, team: 'ORDER', position: 'UTILITY', isMe: false, anonymous: false, form: form(20, 17, 11, recent(412, 'UTILITY', 'WWWWWLLWLW'), roles(['UTILITY', 19])), failed: false, rank: rank('MASTER', 'I', 214), rankRead: true, rankFailed: false },
      { riotId: 'Solenne#0001', championId: 54, team: 'CHAOS', position: 'TOP', isMe: false, anonymous: false, form: form(4, 0, 0, recent(54, null, 'WWLW'), []), failed: false, rank: null, rankRead: true, rankFailed: false },
      { riotId: '', championId: 234, team: 'CHAOS', position: 'JUNGLE', isMe: false, anonymous: true, form: null, failed: false, rank: null, rankRead: false, rankFailed: false },
      { riotId: 'Brambleheart#FR1', championId: 81, team: 'CHAOS', position: 'BOTTOM', isMe: false, anonymous: false, form: null, failed: true, rank: null, rankRead: false, rankFailed: true },
      { riotId: 'Ashvale#LOL', championId: 111, team: 'CHAOS', position: 'UTILITY', isMe: false, anonymous: false, form: form(20, 8, 6, recent(111, 'UTILITY', 'LLLWLWWWLW'), roles(['UTILITY', 8], ['BOTTOM', 5])), failed: false, rank: rank('EMERALD', 'IV', 37), rankRead: true, rankFailed: false },
    ],
  }
}
