import type { ActivityBucket, ActivityMode, ActivitySeries } from '~~/shared/types/activity'

/**
 * The activity heatmap's presentation rules (#927). Pure functions, kept out of
 * the component so the colour steps and the empty-vs-zero split are testable
 * without mounting anything.
 */

/**
 * How many populated steps the scale has. Four, and *discrete*: the first
 * version of this grid faded a continuous alpha between 0.32 and 0.8 over a
 * blend of two quantities at once, which is exactly the recipe for a card where
 * every cell is a slightly different smudge and none of them means anything at a
 * glance. A short ladder of named steps is what makes a contribution grid
 * readable — the eye compares tiles to each other, not to a gradient.
 */
export const ACTIVITY_LEVELS = 4

/**
 * The tile of a period with no games. A real fill, one step above the card
 * surface (`--ui-bg-elevated`, `#1b1b20`), not an outline and not a hole: an
 * idle day is part of the shape of a player's month, and it has to sit in the
 * grid as calmly as GitHub's does.
 *
 * Neutral and unmistakably grey, because the ramp runs light-to-dark and the
 * idle tile can therefore no longer be told apart by lightness alone — it is the
 * *absence of hue* that marks it. Painted clearly above the card surface, too:
 * an idle day that fades into the background reads as a hole in the grid, and a
 * grid with holes in it has no shape to compare against.
 */
export const ACTIVITY_EMPTY_FILL = '#33333b'

/**
 * The one ramp the grid draws, ordered by step — `ACTIVITY_RAMP[0]` is the
 * quietest period, `ACTIVITY_RAMP[3]` the busiest.
 *
 * It runs **light to dark**: a day with one game is a pale rose, a day the
 * player queued all evening is a deep one. That is the reading the product owner
 * has of it — density is weight, and weight is dark — and it is the opposite of
 * GitHub's, which climbs towards a bright green. Worth stating plainly, because
 * the reflex when extending this is to sort the stops the other way.
 *
 * The two middle steps are `rosegold-500` and `rosegold-700` straight off the
 * palette; the ends deliberately overshoot it, one above `rosegold-400` and one
 * below `rosegold-900`, because a contribution grid lives on the distance
 * between its quietest and its loudest tile. Held inside the palette's own range
 * the four steps were four shades of brick and the grid read as one flat block.
 *
 * One hue, and no grey: what the squares answer is *did this player queue, and
 * how much* — not how the games went. An earlier version split the grid over two
 * ramps (rose for a winning period, neutral for a losing one), which spent the
 * loudest channel on the win rate and left half the card looking switched off.
 * The result of a period is still one hover away, and the card's own summary
 * line carries the total; the squares are about presence.
 */
export const ACTIVITY_RAMP = ['#f0a293', '#d9736c', '#a1454a', '#6b2830'] as const

/**
 * Which step of the ramp a cell sits on: `0` for an empty period, `1`..`4` for a
 * played one, scaled on games played against the busiest cell in the grid.
 *
 * `maxGames <= 1` means every cell in the series is a single game (the per-game
 * view), where volume carries no information at all. That view is the one place
 * the step falls back to the result — a won game on the deepest step, a lost one
 * two steps lighter — so the strip still has a shape instead of being a flat
 * rose bar.
 */
export function activityCellLevel(bucket: ActivityBucket, maxGames: number): number {
  if (bucket.games <= 0) return 0
  if (maxGames <= 1) return bucket.wins > 0 ? ACTIVITY_LEVELS : ACTIVITY_LEVELS - 2

  const share = Math.min(1, bucket.games / maxGames)
  return Math.min(ACTIVITY_LEVELS, Math.max(1, Math.ceil(share * ACTIVITY_LEVELS)))
}

/**
 * Fill for one cell, or `null` when the cell has no games.
 *
 * A `null` return is the empty cell, and the caller paints it
 * {@link ACTIVITY_EMPTY_FILL} — `games: 0` and `winRate: 0` are different facts
 * and must never render alike.
 */
export function activityCellFill(bucket: ActivityBucket, maxGames: number): string | null {
  if (bucket.games <= 0) return null

  return ACTIVITY_RAMP[activityCellLevel(bucket, maxGames) - 1]!
}

/**
 * The busiest cell in the series — the denominator of the intensity scale above.
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
 * The short caption printed under a tile in the views that carry few enough
 * cells to label them (week, patch). A patch is its own number; a week is the
 * day it starts on.
 */
export function activityBucketCaption(bucket: ActivityBucket, mode: ActivityMode): string {
  if (mode === 'patch') return bucket.key
  if (!bucket.startUtc) return bucket.key

  return new Date(bucket.startUtc).toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    timeZone: 'UTC',
  })
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

/**
 * The period a series speaks for, as one line — `Jul 17 – Aug 13`. Read off the
 * buckets rather than the series' `coverage*` fields so it can never disagree
 * with the squares actually on screen. `null` when the series carries no dates
 * at all (the patch view keys on patch numbers, not on time).
 */
export function activityCoverageLabel(series: ActivitySeries): string | null {
  const dated = series.buckets.filter(bucket => bucket.startUtc)
  const first = dated[0]?.startUtc
  const last = dated[dated.length - 1]?.startUtc
  if (!first || !last) return null

  const format = (iso: string) =>
    new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' })

  return first === last ? format(first) : `${format(first)} – ${format(last)}`
}
