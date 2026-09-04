import type { ActivityBucket, ActivityMode, ActivitySeries } from '~~/shared/types/activity'

/**
 * The activity heatmap's presentation rules (#927, reshaped in #1473). Pure
 * functions, kept out of the component so the colour steps and the empty-vs-zero
 * split are testable without mounting anything.
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
 * The tile of a day with no games. A real fill, one step above the card
 * surface (`--ui-bg-elevated`, `#1b1b20`), not an outline and not a hole: an
 * idle day is part of the shape of a patch, and it has to sit in the grid as
 * calmly as GitHub's does.
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
 * quietest day, `ACTIVITY_RAMP[3]` the busiest.
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
 * The result of a day is still one hover away, and the card's own summary line
 * carries the total; the squares are about presence.
 */
export const ACTIVITY_RAMP = ['#f0a293', '#d9736c', '#a1454a', '#6b2830'] as const

/**
 * True when a series' cells are single games rather than days — the `day`
 * window, which is narrow enough that there are no days left to draw.
 *
 * This is what the colour scale branches on, and it is deliberately read off the
 * mode rather than inferred from the data. Inferring it (`maxGames <= 1`) is
 * what the first version did, and it mislabels the honest case of a patch where
 * the player never queued twice on the same day: those are days, they carry
 * volume information, and they must not silently start being coloured by result.
 */
export function activityCellsAreGames(mode: ActivityMode): boolean {
  return mode === 'day'
}

/**
 * Which step of the ramp a cell sits on: `0` for a day with no games, `1`..`4`
 * for a played one, scaled on games played against the busiest cell in the grid.
 *
 * On the per-game window volume carries no information at all — every cell is
 * one game — so that window is the one place the step falls back to the result:
 * a won game on the deepest step, a lost one two steps lighter, so the strip
 * still has a shape instead of being a flat rose bar.
 */
export function activityCellLevel(bucket: ActivityBucket, maxGames: number, perGame = false): number {
  if (bucket.games <= 0) return 0
  if (perGame) return bucket.wins > 0 ? ACTIVITY_LEVELS : ACTIVITY_LEVELS - 2

  const share = maxGames <= 0 ? 1 : Math.min(1, bucket.games / maxGames)
  return Math.min(ACTIVITY_LEVELS, Math.max(1, Math.ceil(share * ACTIVITY_LEVELS)))
}

/**
 * Fill for one cell, or `null` when the cell has no games.
 *
 * A `null` return is the empty cell, and the caller paints it
 * {@link ACTIVITY_EMPTY_FILL} — `games: 0` and `winRate: 0` are different facts
 * and must never render alike.
 */
export function activityCellFill(bucket: ActivityBucket, maxGames: number, perGame = false): string | null {
  if (bucket.games <= 0) return null

  return ACTIVITY_RAMP[activityCellLevel(bucket, maxGames, perGame) - 1]!
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
 * Human label for a cell, used as the tooltip heading: the UTC day on a calendar
 * window, the day and time on the per-game one, where several cells share a day.
 *
 * The locale is pinned to `en-US` everywhere — the app has no i18n, and an
 * implicit locale is what makes a server render disagree with the client one.
 */
export function activityBucketLabel(bucket: ActivityBucket, mode: ActivityMode): string {
  const start = new Date(bucket.startUtc)

  if (activityCellsAreGames(mode)) {
    return start.toLocaleString('en-US', {
      month: 'short',
      day: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
      timeZone: 'UTC',
    })
  }

  return start.toLocaleDateString('en-US', {
    weekday: 'short',
    month: 'short',
    day: 'numeric',
    timeZone: 'UTC',
  })
}

/**
 * The short caption printed under a tile, or `null` for a window whose cells are
 * too many (or too undated) to label.
 *
 * A week is seven days, so the weekday is the useful handle — `Mon`, `Tue`. A
 * patch is a fortnight or so, where the weekday repeats and the date does not,
 * so it captions the day of the month instead. The per-game window captions
 * nothing: a run of games is read as a strip, and stamping a time under each one
 * turns it into a table.
 */
export function activityBucketCaption(bucket: ActivityBucket, mode: ActivityMode): string | null {
  const start = new Date(bucket.startUtc)

  if (mode === 'week') {
    return start.toLocaleDateString('en-US', { weekday: 'short', timeZone: 'UTC' })
  }

  if (mode === 'patch') {
    return start.toLocaleDateString('en-US', { day: 'numeric', timeZone: 'UTC' })
  }

  return null
}

/**
 * Result line for a cell's tooltip: always wins over games played, whatever the
 * window. A single game used to print "Victory" / "Defeat", which made the
 * tooltip change shape between two neighbouring squares and forced the reader to
 * re-parse it; `1/1 · 100%` says the same thing in the format every other cell
 * already uses.
 *
 * An empty day says so in words instead of printing "0/0 · 0%" — a fabricated
 * rate is exactly what the empty state exists to avoid.
 */
export function activityBucketResult(bucket: ActivityBucket): string {
  if (bucket.games <= 0 || bucket.winRate === null) return 'No games'

  const rate = `${Math.round(bucket.winRate * 100)}%`
  return `${bucket.wins}/${bucket.games} · ${rate}`
}

/**
 * The span a series speaks for, as one line — `Jul 17 – Aug 13`. Read off the
 * buckets rather than the series' `coverage*` fields so it can never disagree
 * with the squares actually on screen. `null` when the series carries no cells.
 */
export function activityCoverageLabel(series: ActivitySeries): string | null {
  const first = series.buckets[0]?.startUtc
  const last = series.buckets[series.buckets.length - 1]?.startUtc
  if (!first || !last) return null

  const format = (iso: string) =>
    new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' })

  return format(first) === format(last) ? format(first) : `${format(first)} – ${format(last)}`
}
