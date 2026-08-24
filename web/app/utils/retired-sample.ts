import { formatMatchDay } from '~/utils/match-history'

/**
 * The qualifier shown next to a main whose games have aged out of retention
 * (#1216), and the tooltip that explains it.
 *
 * Kept separate from the component because the honesty of the wording is the
 * whole point: the figures are not wrong and not "not yet available" — they are
 * the last real measurement, and the only thing missing is the games behind
 * them. Saying "10 games" unqualified is what let a profile promise a build the
 * champion page had nothing to show for.
 *
 * Returns null when there is no usable date, in which case the caller shows the
 * plain count — a badge reading "as of Invalid Date" would be worse than none.
 */
export function formatRetiredSample(
  measuredAtUtc: string | null | undefined,
  now: Date = new Date(),
): { suffix: string, tooltip: string } | null {
  if (!measuredAtUtc) return null

  const day = formatMatchDay(measuredAtUtc, now)
  if (!day) return null

  return {
    suffix: `as of ${day}`,
    tooltip: `Last measured on ${day}. The games behind this count have since aged out of `
      + `our retention window, so it reflects what we held then, not what this player has played since.`,
  }
}
