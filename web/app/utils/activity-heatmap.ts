import type { ActivityBucket, ActivityMode, ActivitySeries } from '~~/shared/types/activity'

/**
 * The activity heatmap's presentation rules (#927). Pure functions, kept out of
 * the component so the colour thresholds and the empty-vs-zero split are
 * testable without mounting anything.
 */

/**
 * Win end of the scale — `--color-data-good` (see `assets/css/main.css`). The
 * grid is read the way GitHub's contribution grid is: hue carries the sign,
 * intensity carries the weight.
 *
 * This used to be `rosegold-400`, on the reasoning that the app was rose-gold
 * only on surfaces and a second hue would have been an intrusion. That rule is
 * gone: measurements now have their own cold→warm axis precisely so a reader
 * never has to work out whether a colour means "good" or "brand". A win cell is
 * the same teal as an S tier and an above-average win rate.
 */
export const ACTIVITY_WIN_RGB = [58, 214, 196] as const

/**
 * Loss end of the scale — `--color-data-bad`. Formerly `mauve-400`, a near
 * neutral chosen to desaturate towards the shell rather than take a second hue.
 * On the data axis the losing side has a colour of its own, and amber is far
 * enough from teal that the sign of a period reads at 11 px without relying on
 * intensity.
 */
export const ACTIVITY_LOSS_RGB = [240, 161, 60] as const

/**
 * Alpha of the least emphatic populated cell — a coin-flip, single-game period.
 * Floored well above transparent on purpose: the empty cell is itself a faint
 * tile, so a played period fading below it would be read as "no games".
 */
export const ACTIVITY_MIN_ALPHA = 0.32

/** Alpha of the most emphatic cell — the busiest period, fully decided. */
export const ACTIVITY_MAX_ALPHA = 0.8

/**
 * Fill for one cell, or `null` when the cell has no games.
 *
 * Two channels, chosen so neither can mask the other:
 * - **hue** is which side of 50% the win rate sits on. A cell is rose or muted,
 *   never an interpolated mud in between, so the sign of the result is readable
 *   at 11 px.
 * - **alpha** is how much the cell is worth reading: half how decided the period
 *   was (distance from 50%), half how busy it was relative to the grid's
 *   heaviest cell. A single 100% day therefore stays visibly weaker than a
 *   ten-game 100% day without disappearing.
 *
 * A `null` return is the empty cell and the caller must render it as an outline,
 * not as a 0% fill — `games: 0` and `winRate: 0` are different facts.
 */
export function activityCellFill(bucket: ActivityBucket, maxGames: number): string | null {
  if (bucket.games <= 0 || bucket.winRate === null) return null

  const [r, g, b] = bucket.winRate >= 0.5 ? ACTIVITY_WIN_RGB : ACTIVITY_LOSS_RGB

  // 0 at a coin flip, 1 at a clean sweep either way.
  const decisiveness = Math.abs(bucket.winRate - 0.5) * 2
  // Relative volume. `maxGames <= 1` means every cell in the grid is a single
  // game (the per-game series), so volume carries no information and is pinned
  // at 1 rather than dividing by a degenerate denominator.
  const volume = maxGames <= 1 ? 1 : Math.min(1, bucket.games / maxGames)

  const weight = 0.5 * decisiveness + 0.5 * volume
  const alpha = ACTIVITY_MIN_ALPHA + (ACTIVITY_MAX_ALPHA - ACTIVITY_MIN_ALPHA) * weight

  return `rgba(${r}, ${g}, ${b}, ${alpha.toFixed(3)})`
}

/**
 * The busiest cell in the series — the denominator of the volume channel above.
 * Returns 0 for an empty series.
 */
export function activityMaxGames(series: ActivitySeries): number {
  let max = 0
  for (const bucket of series.buckets) {
    if (bucket.games > max) max = bucket.games
  }
  return max
}

/**
 * Human label for a cell, used as the tooltip heading.
 *
 * The patch series keys on the patch itself, so it is already the label. The
 * others key on a date and get an explicit `en-US` format — the app has no i18n
 * and pins the locale everywhere to keep server and client renders identical.
 */
export function activityBucketLabel(bucket: ActivityBucket, mode: ActivityMode): string {
  if (mode === 'patch') return `Patch ${bucket.key}`
  if (!bucket.startUtc) return bucket.key

  const start = new Date(bucket.startUtc)
  const day = start.toLocaleDateString('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' })

  if (mode === 'week') {
    const end = new Date(start.getTime() + 6 * 24 * 60 * 60 * 1000)
    const endDay = end.toLocaleDateString('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' })
    return `${day} – ${endDay}`
  }

  if (mode === 'game') {
    return start.toLocaleDateString('en-US', {
      month: 'short',
      day: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
      timeZone: 'UTC',
    })
  }

  return day
}

/**
 * Result line for a cell's tooltip: always wins over games played, whatever the
 * granularity. A single game used to print "Victory" / "Defeat", which made the
 * tooltip change shape between two neighbouring squares and forced the reader to
 * re-parse it; `1/1 · 100%` says the same thing in the format every other cell
 * already uses.
 *
 * An empty period says so in words instead of printing "0/0 · 0%" — a fabricated
 * rate is exactly what the empty state exists to avoid.
 */
export function activityBucketResult(bucket: ActivityBucket): string {
  if (bucket.games <= 0 || bucket.winRate === null) return 'No games'

  const rate = `${Math.round(bucket.winRate * 100)}%`
  return `${bucket.wins}/${bucket.games} · ${rate}`
}
