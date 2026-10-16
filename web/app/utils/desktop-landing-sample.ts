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
 * Our side's chance to win in the sample, from the model the overlay itself
 * runs (`#common/utils/win-probability`): every lane even, three turrets and
 * two drakes against one and one.
 */
export function sampleWinProbability(): number {
  const side = (turrets: number, dragons: number) => ({ turrets, inhibitorsDown: 0, dragons, baron: false, elder: false })
  return winProbability({ leads: [], ours: side(3, 2), theirs: side(1, 1), clock: SAMPLE_CLOCK })
}

/** A champion's square icon on Data Dragon, from its key (`Ahri`); null until the patch is known. */
export function championIconUrl(key: string, patch: string | null | undefined): string | null {
  return patch ? `https://ddragon.leagueoflegends.com/cdn/${patch}/img/champion/${key}.png` : null
}
