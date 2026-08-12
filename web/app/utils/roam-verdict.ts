/**
 * Turns the roam metric (#536) into the one thing a reader acts on: is this
 * champion a roamer, yes or no.
 *
 * The API still returns the per-game out-of-lane kill participations at
 * @5/@10/@15 — the curve is worth keeping in the database — but the page only
 * shows the call, as a single badge next to the win rate. A three-bar chart for
 * three cumulative numbers said much less than it took up, and "balanced" is not
 * a fact anyone came to the page for.
 *
 * Where "roamer" starts is a product judgement, not a measurement, so it lives
 * here rather than in the backend — same split as {@link laneVerdict}.
 */

/**
 * Average out-of-lane kills + assists by 15 minutes at which a champion reads as
 * a roamer. A full extra kill participation away from lane every game is a
 * playstyle, not variance; below it the number is real but says nothing the lane
 * itself doesn't.
 */
export const ROAMER_KP15 = 1.5

export interface RoamVerdict {
  /** Badge text — already a noun, so it stands alone next to the win rate. */
  label: string
  /** What the badge is claiming, and off which number. Sits in its tooltip. */
  tooltip: string
}

/**
 * The roam badge for a champion, or `null` when there is nothing to flag — the
 * metric is unmeasured (below the backend's sample floor, or JUNGLE, which has
 * no own lane to leave) or the champion simply stays in it. Only roamers get a
 * badge: "not a roamer" is the default every champion page already implies.
 */
export function roamVerdict(roamKp15: number | null | undefined): RoamVerdict | null {
  if (roamKp15 === null || roamKp15 === undefined || roamKp15 < ROAMER_KP15) return null

  return {
    label: 'Roamer',
    tooltip: `${roamKp15.toFixed(1)} out-of-lane kills + assists per game by 15 min`,
  }
}
