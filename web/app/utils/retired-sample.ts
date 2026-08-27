import { formatMatchDay } from '~/utils/match-history'

/**
 * The tooltip shown next to a main whose games have aged out of retention
 * (#1216).
 *
 * Kept separate from the component because the honesty of the wording is the
 * whole point: the figures are not wrong and not "not yet available" — they are
 * the last real measurement, and the only thing missing is the games behind
 * them. Saying "10 games" unqualified is what let a profile promise a build the
 * champion page had nothing to show for.
 *
 * The date used to be printed inline as well, as an `as of 1 Aug` suffix beside
 * the count; #1275 dropped it. The warning glyph and this tooltip carry the
 * qualifier on their own, and the row's job is the count — an every-row date on
 * a card of five mains read as clutter, not as candour.
 *
 * Returns null when there is no usable date, in which case the caller shows the
 * plain count with no marker at all — a tooltip saying "Last measured on
 * Invalid Date" would be worse than none.
 */
export function retiredSampleTooltip(
  measuredAtUtc: string | null | undefined,
  now: Date = new Date(),
): string | null {
  if (!measuredAtUtc) return null

  const day = formatMatchDay(measuredAtUtc, now)
  if (!day) return null

  return `Last measured on ${day}. The games behind this count have since aged out of `
    + `our retention window, so it reflects what we held then, not what this player has played since.`
}
