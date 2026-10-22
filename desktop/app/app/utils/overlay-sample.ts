import type { GamePlayer, GameState, GameTeam } from '~/types/game'
import type { ItemRouteStep } from '~/utils/build-path'
import type { NextItemOrderStep } from '~/utils/next-item'
import type { PaceReference } from '~/utils/pace-benchmark'

/**
 * What the overlay's panels show while they are placed (the settings'
 * preview) and no game is there to read: a game mid-way through, made up but
 * shaped like a real one, so each panel is placed at the size it will have
 * over the game rather than as a sentence about itself. Never shown over a
 * game: a real game always wins.
 */

const sampleStep = (itemId: number, cost: number, rank: number, components: ItemRouteStep[] = []): ItemRouteStep =>
  ({ itemId, cost, rank, share: null, measured: true, components })

/**
 * The next item the sample shows (#2183): Zhonya's Hourglass with an
 * Amplifying Tome held and 750 gold, its path in a made-up order — Seeker's
 * Armguard (Tome, Cloth Armor, Tome) then the Rod — so the three states show:
 * the Tome held, the Cloth Armor and the second Tome bought now, the Rod later.
 */
export const SAMPLE_NEXT_ITEM = {
  itemId: 3157,
  held: [1052],
  gold: 750,
  route: [
    sampleStep(2420, 1600, 1, [sampleStep(1052, 400, 1), sampleStep(1029, 300, 2), sampleStep(1052, 400, 3)]),
    sampleStep(1058, 1200, 2),
  ],
}

/** The build row under it (#2101): one item and the boots done, the sample's next item, the rest to come. */
export const SAMPLE_BUILD_ORDER: NextItemOrderStep[] = [
  { itemId: 6655, isBoots: false, state: 'Completed' },
  { itemId: 3020, isBoots: true, state: 'Completed' },
  { itemId: SAMPLE_NEXT_ITEM.itemId, isBoots: false, state: 'Next' },
  { itemId: 3089, isBoots: false, state: 'Upcoming' },
  { itemId: 3135, isBoots: false, state: 'Upcoming' },
  { itemId: 4645, isBoots: false, state: 'Upcoming' },
]

/** The tier reference the pace panel's sample shows beside the sample game's pace (#1912). */
export const SAMPLE_PACE_REFERENCE: PaceReference = {
  tier: 'DIAMOND',
  cs: { median: 7.3, standing: 'above' },
  gold: { median: 425, standing: 'within' },
  csCurve: [3, 5, 8, 13, 16, 21, 26].map((minute, index) => ({ minute, value: [4.6, 5.9, 6.5, 6.9, 7.1, 7.2, 7.3][index]! })),
}

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
  creepScore: position === 'UTILITY' ? 30 : position === 'JUNGLE' ? 150 : 200,
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
      order: { turrets: 3, inhibitors: [], dragons: 2, drakes: ['infernal', 'mountain'], baronUntil: null, elderUntil: null },
      chaos: { turrets: 1, inhibitors: [], dragons: 1, drakes: ['hextech'], baronUntil: null, elderUntil: null },
      // At the sample's 26:00: the next drake in 1:15, the grubs and the
      // Herald done, Baron up since 20:00.
      timers: {
        dragon: { at: 1635, until: null },
        elder: false,
        drake: 'ocean',
        grubs: { at: null, until: null },
        herald: { at: null, until: null },
        baron: { at: 1200, until: null },
      },
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
