import type { GamePlayer, GameState, GameTeam } from '~/types/game'
import type { LoadingView, RecentGame } from '~/types/loading'

/**
 * What the overlay's panels show while they are placed (the settings'
 * preview) and no game is there to read: a game mid-way through, made up but
 * shaped like a real one, so each panel is placed at the size it will have
 * over the game rather than as a sentence about itself. Never shown over a
 * game: a real game always wins.
 */

/** The next item the sample shows, and the gold it still needs. */
export const SAMPLE_NEXT_ITEM = { itemId: 3157, missing: 650 } as const

const player = (champion: string, team: GameTeam, position: string, items: number[], isMe = false): GamePlayer => ({
  riotId: '',
  champion,
  championName: champion,
  team,
  position,
  isMe,
  isBot: false,
  spells: [],
  level: 14,
  items: items.map((itemId, slot) => ({ itemId, slot, count: 1 })),
  kills: 0,
  deaths: 0,
  assists: 0,
  dead: false,
  respawnAt: null,
})

export function sampleGame(): GameState {
  return {
    revision: 0,
    gameTime: 1560,
    gameMode: 'CLASSIC',
    mapNumber: 11,
    myTeam: 'ORDER',
    players: [
      player('Aatrox', 'ORDER', 'TOP', [6692, 3047, 3053, 3071]),
      player('Vi', 'ORDER', 'JUNGLE', [3078, 3047, 3053]),
      player('Ahri', 'ORDER', 'MIDDLE', [6655, 3020, 4645, 3089], true),
      player('Jinx', 'ORDER', 'BOTTOM', [3032, 3006, 3031, 1018]),
      player('Thresh', 'ORDER', 'UTILITY', [3869, 3117, 3190]),
      player('Malphite', 'CHAOS', 'TOP', [3068, 3047, 3075]),
      player('Viego', 'CHAOS', 'JUNGLE', [3153, 3006, 6610]),
      player('Orianna', 'CHAOS', 'MIDDLE', [3118, 3020, 3089]),
      player('Ezreal', 'CHAOS', 'BOTTOM', [3042, 3078, 3158, 6694]),
      player('Nautilus', 'CHAOS', 'UTILITY', [3870, 3047, 3190, 3109]),
    ],
    gold: 600,
    objectives: {
      order: { turrets: 3, inhibitors: [], dragons: 2, baronUntil: null, elderUntil: null },
      chaos: { turrets: 1, inhibitors: [], dragons: 1, baronUntil: null, elderUntil: null },
    },
    pace: {
      samples: [
        { minute: 3, cs: 15, gold: 1311 },
        { minute: 5, cs: 31, gold: 2207 },
        { minute: 8, cs: 55, gold: 3674 },
        { minute: 13, cs: 94, gold: 5591 },
        { minute: 16, cs: 120, gold: 7197 },
        { minute: 21, cs: 162, gold: 9312 },
        { minute: 26, cs: 204, gold: 11433 },
      ],
    },
  }
}

export function sampleLoadingView(): LoadingView {
  const now = Date.now()
  const recent = (championId: number, position: string, results: string): RecentGame[] =>
    [...results].map((result, index) => ({
      championId,
      position,
      win: result === 'W',
      kills: (index * 3 + 4) % 11,
      deaths: (index * 5 + 2) % 8,
      assists: (index * 7 + 3) % 13,
      playedAt: now - (index + 1) * 5 * 3_600_000,
    }))
  const at = (riotId: string, championId: number, team: GameTeam, position: string, championGames: number, championWins: number, results: string) => ({
    riotId,
    championId,
    team,
    position,
    isMe: false,
    anonymous: false,
    form: { games: 20, championGames, championWins, recent: recent(championId, position, results) },
    failed: false,
  })
  return {
    players: [
      at('Harrowgate#0001', 266, 'ORDER', 'TOP', 11, 7, 'WLWWLWLWWL'),
      at('Lumen#0001', 254, 'ORDER', 'JUNGLE', 6, 3, 'LWLWWLLWLW'),
      { ...at('You#TRUE', 103, 'ORDER', 'MIDDLE', 14, 9, 'WWWLWLLWWL'), isMe: true },
      at('Orrin#EUW', 222, 'ORDER', 'BOTTOM', 9, 5, 'WLLWWLWLWW'),
      at('Nyrox#LOL', 412, 'ORDER', 'UTILITY', 17, 11, 'WWWWWLLWLW'),
      at('Solenne#0001', 54, 'CHAOS', 'TOP', 4, 2, 'WWLWLLWLWL'),
      at('Quillfire#0001', 234, 'CHAOS', 'JUNGLE', 8, 4, 'LWWLWLWLLW'),
      at('Wrenfield#FR1', 61, 'CHAOS', 'MIDDLE', 2, 0, 'LLLLWWLWLW'),
      at('Brambleheart#FR1', 81, 'CHAOS', 'BOTTOM', 12, 6, 'WLWLWWLLWL'),
      at('Ashvale#LOL', 111, 'CHAOS', 'UTILITY', 8, 6, 'LLWWLWWWLW'),
    ],
  }
}
