import type { LoadingView, RecentGame } from '~/types/loading'

/**
 * The loading screen for a browser-only `npm run dev`: the late-game
 * scenario's ten players, with forms written by hand — one still being read,
 * one the client could not read, one anonymous. Dev only: `useLoadingPlayers` imports it
 * behind `import.meta.dev`, so no build carries it.
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
  const form = (games: number, championGames: number, championWins: number, latest: RecentGame[]) =>
    ({ games, championGames, championWins, recent: latest })
  return {
    players: [
      { riotId: 'Synthetic#TAPE', championId: 103, team: 'ORDER', position: 'MIDDLE', isMe: true, anonymous: false, form: form(20, 14, 9, recent(103, 'MIDDLE', 'WWWLWLLWWL')), failed: false },
      { riotId: 'Wrenfield#FR1', championId: 61, team: 'CHAOS', position: 'MIDDLE', isMe: false, anonymous: false, form: form(20, 2, 0, recent(61, 'MIDDLE', 'LLLLWWLWLW')), failed: false },
      { riotId: 'Harrowgate#0001', championId: 266, team: 'ORDER', position: 'TOP', isMe: false, anonymous: false, form: form(18, 11, 7, recent(266, 'TOP', 'WLWWLWLWWL')), failed: false },
      { riotId: 'Lumen#0001', championId: 254, team: 'ORDER', position: 'JUNGLE', isMe: false, anonymous: false, form: form(20, 6, 3, recent(254, 'JUNGLE', 'LWLWWLLWLW')), failed: false },
      { riotId: 'Orrin#EUW', championId: 222, team: 'ORDER', position: 'BOTTOM', isMe: false, anonymous: false, form: null, failed: false },
      { riotId: 'Nyrox#LOL', championId: 412, team: 'ORDER', position: 'UTILITY', isMe: false, anonymous: false, form: form(20, 17, 11, recent(412, 'UTILITY', 'WWWWWLLWLW')), failed: false },
      { riotId: 'Solenne#0001', championId: 54, team: 'CHAOS', position: 'TOP', isMe: false, anonymous: false, form: form(4, 0, 0, recent(54, null, 'WWLW')), failed: false },
      { riotId: '', championId: 234, team: 'CHAOS', position: 'JUNGLE', isMe: false, anonymous: true, form: null, failed: false },
      { riotId: 'Brambleheart#FR1', championId: 81, team: 'CHAOS', position: 'BOTTOM', isMe: false, anonymous: false, form: null, failed: true },
      { riotId: 'Ashvale#LOL', championId: 111, team: 'CHAOS', position: 'UTILITY', isMe: false, anonymous: false, form: form(20, 8, 6, recent(111, 'UTILITY', 'LLWWLWWWLW')), failed: false },
    ],
  }
}
