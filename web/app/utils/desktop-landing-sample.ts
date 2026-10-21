import { winProbability } from '#common/utils/win-probability'

/**
 * What `/download` draws the app's panels with (#2072): the shape of the app's own
 * overlay preview sample (`desktop/app/app/utils/overlay-sample.ts`), moved to the
 * real game the page shows under them (`public/desktop/game.webp`, one of our own
 * Yone games at 13:23) so no panel contradicts the HUD beside it: our champion,
 * the clock, the creep score. The values are a sample all the same, and the page
 * says so; the panels' layout and wording stay the app's.
 */

/** Game clock of the sample, in seconds (13:23, the frame's). */
export const SAMPLE_CLOCK = 803

/** The two lines up, lane by lane (top → support); ours is the middle. */
export const SAMPLE_ALLIES = ['Aatrox', 'Vi', 'Yone', 'Jinx', 'Thresh'] as const
export const SAMPLE_ENEMIES = ['Malphite', 'Viego', 'Orianna', 'Ezreal', 'Nautilus'] as const

/** Each player's items in the sample, lane by lane, ours then theirs. */
export const SAMPLE_ITEMS: Record<'ally' | 'enemy', number[][]> = {
  ally: [[6692, 3047, 3053, 3071], [3078, 3047, 3053], [6672, 3006, 1018], [3032, 3006, 3031, 1018], [3869, 3117, 3190]],
  enemy: [[3068, 3047, 3075], [3153, 3006, 6610], [3118, 3020, 3089], [3042, 3078, 3158, 6694], [3870, 3047, 3190, 3109]],
}

/** The next item the sample shows, and the gold it still needs. */
export const SAMPLE_NEXT_ITEM = { itemId: 3031, missing: 650 } as const

/**
 * The build row under it (#2101), as the app's sample draws it: an item and the boots
 * done, the next item ringed, the rest still to come.
 */
export const SAMPLE_BUILD_ORDER: readonly { itemId: number, state: 'Completed' | 'Next' | 'Upcoming' }[] = [
  { itemId: 6672, state: 'Completed' },
  { itemId: 3006, state: 'Completed' },
  { itemId: SAMPLE_NEXT_ITEM.itemId, state: 'Next' },
  { itemId: 3036, state: 'Upcoming' },
  { itemId: 3072, state: 'Upcoming' },
  { itemId: 3026, state: 'Upcoming' },
]

/** Our creep score and gold at each whole minute the shell keeps (`live_client::pace`). */
export const SAMPLE_PACE = [
  { minute: 3, cs: 15, gold: 1311 },
  { minute: 5, cs: 31, gold: 2207 },
  { minute: 8, cs: 55, gold: 3674 },
  { minute: 10, cs: 75, gold: 4310 },
  { minute: 13, cs: 99, gold: 5720 },
] as const

/** The tier reference beside the pace: the median of Diamond games at each minute. */
export const SAMPLE_PACE_REFERENCE = {
  tier: 'Diamond',
  cs: { median: 7.1, standing: 'above' },
  gold: { median: 432, standing: 'within' },
  csCurve: [4.6, 5.9, 6.5, 6.9, 7.1],
} as const

/**
 * The objectives strip's sample at 13:23 (#2078's rules, #2184's strip): a drake
 * each side, the Rift turned Infernal so the next one is named, due at 16:00; the
 * grubs gone, so the Herald counts down to 15:00, and Baron, due at 20:00, waits
 * on the Herald. Each spawn is a game time, in seconds, as the shell sends it.
 */
export const SAMPLE_OBJECTIVES = {
  drakes: { ally: ['cloud'], enemy: ['infernal'] },
  next: 'infernal',
  spawns: { dragon: 960, herald: 900, baron: 1200 },
} as const

/**
 * Our side's chance to win in the sample, from the model the overlay itself
 * runs (`#common/utils/win-probability`): every lane even, three turrets against
 * one, a drake each.
 */
export function sampleWinProbability(): number {
  const dragons = (side: 'ally' | 'enemy') => SAMPLE_OBJECTIVES.drakes[side].length
  const side = (turrets: number, count: number) => ({ turrets, inhibitorsDown: 0, dragons: count, baron: false, elder: false })
  return winProbability({ leads: [], ours: side(3, dragons('ally')), theirs: side(1, dragons('enemy')), clock: SAMPLE_CLOCK })
}

/** A champion's square icon on Data Dragon, from its key (`Ahri`); null until the patch is known. */
export function championIconUrl(key: string, patch: string | null | undefined): string | null {
  return patch ? `https://ddragon.leagueoflegends.com/cdn/${patch}/img/champion/${key}.png` : null
}
