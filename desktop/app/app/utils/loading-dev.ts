import type { LoadingView } from '~/types/loading'

/**
 * The loading screen for a browser-only `npm run dev`: the late-game
 * scenario's ten players, with forms written by hand — one still being read,
 * one the client could not read. Dev only: `useLoadingPlayers` imports it
 * behind `import.meta.dev`, so no build carries it.
 */
export function devLoadingView(): LoadingView {
  const form = (games: number, championGames: number, championWins: number, streak: number) =>
    ({ games, championGames, championWins, streak })
  return {
    players: [
      { riotId: 'Synthetic#TAPE', championId: 103, team: 'ORDER', position: 'MIDDLE', isMe: true, form: form(20, 14, 9, 3), failed: false },
      { riotId: 'Wrenfield#FR1', championId: 61, team: 'CHAOS', position: 'MIDDLE', isMe: false, form: form(20, 2, 0, -4), failed: false },
      { riotId: 'Harrowgate#0001', championId: 266, team: 'ORDER', position: 'TOP', isMe: false, form: form(18, 11, 7, 1), failed: false },
      { riotId: 'Lumen#0001', championId: 254, team: 'ORDER', position: 'JUNGLE', isMe: false, form: form(20, 6, 3, -1), failed: false },
      { riotId: 'Orrin#EUW', championId: 222, team: 'ORDER', position: 'BOTTOM', isMe: false, form: null, failed: false },
      { riotId: 'Nyrox#LOL', championId: 412, team: 'ORDER', position: 'UTILITY', isMe: false, form: form(20, 17, 11, 5), failed: false },
      { riotId: 'Solenne#0001', championId: 54, team: 'CHAOS', position: 'TOP', isMe: false, form: form(20, 0, 0, 2), failed: false },
      { riotId: 'Quillfire#0001', championId: 234, team: 'CHAOS', position: 'JUNGLE', isMe: false, form: form(15, 9, 6, 0), failed: false },
      { riotId: 'Brambleheart#FR1', championId: 81, team: 'CHAOS', position: 'BOTTOM', isMe: false, form: null, failed: true },
      { riotId: 'Ashvale#LOL', championId: 111, team: 'CHAOS', position: 'UTILITY', isMe: false, form: form(20, 8, 6, -2), failed: false },
    ],
  }
}
