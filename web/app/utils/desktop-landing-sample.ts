import { winProbability } from '#common/utils/win-probability'

/**
 * What `/download` draws the app's panels with (#2072): the game the app's own
 * overlay preview places its panels over (`desktop/app/app/utils/overlay-sample.ts`),
 * a game 26 minutes in, made up but shaped like a real one. The page labels every
 * drawing that uses it as a sample, so none of these values reads as a claim.
 * Keep the two copies in step when the app's sample changes: the page promises
 * the panels the app draws.
 */

/** Game clock of the sample, in seconds (26:00). */
export const SAMPLE_CLOCK = 1560

/** Our champion, its lane opponent, and the two lines up, lane by lane (top → support). */
export const SAMPLE_ALLIES = ['Aatrox', 'Vi', 'Ahri', 'Jinx', 'Thresh'] as const
export const SAMPLE_ENEMIES = ['Malphite', 'Viego', 'Orianna', 'Ezreal', 'Nautilus'] as const
export const SAMPLE_ME = 'Ahri'

/** Each player's items in the sample, lane by lane, ours then theirs. */
export const SAMPLE_ITEMS: Record<'ally' | 'enemy', number[][]> = {
  ally: [[6692, 3047, 3053, 3071], [3078, 3047, 3053], [6655, 3020, 4645, 3089], [3032, 3006, 3031, 1018], [3869, 3117, 3190]],
  enemy: [[3068, 3047, 3075], [3153, 3006, 6610], [3118, 3020, 3089], [3042, 3078, 3158, 6694], [3870, 3047, 3190, 3109]],
}

/** The next item the sample shows, and the gold it still needs. */
export const SAMPLE_NEXT_ITEM = { itemId: 3157, missing: 650 } as const

/** Our creep score and gold at each whole minute the shell keeps (`live_client::pace`). */
export const SAMPLE_PACE = [
  { minute: 3, cs: 15, gold: 1311 },
  { minute: 5, cs: 31, gold: 2207 },
  { minute: 8, cs: 55, gold: 3674 },
  { minute: 13, cs: 94, gold: 5591 },
  { minute: 16, cs: 120, gold: 7197 },
  { minute: 21, cs: 162, gold: 9312 },
  { minute: 26, cs: 204, gold: 11433 },
] as const

/** The tier reference beside the pace: the median of Diamond games at each minute. */
export const SAMPLE_PACE_REFERENCE = {
  tier: 'Diamond',
  cs: { median: 7.3, standing: 'above' },
  gold: { median: 425, standing: 'within' },
  csCurve: [4.6, 5.9, 6.5, 6.9, 7.1, 7.2, 7.3],
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

/** A champion's default splash art (not versioned). */
export function championSplashUrl(key: string): string {
  return `https://ddragon.leagueoflegends.com/cdn/img/champion/splash/${key}_0.jpg`
}
