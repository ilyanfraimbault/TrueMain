import type { ActivityBucket, ActivityMode, ActivitySeries } from '~~/shared/types/activity'

/**
 * The activity heatmap's presentation rules (#927). Pure functions, kept out of
 * the component so the colour thresholds and the empty-vs-zero split are
 * testable without mounting anything.
 */

/**
 * Win end of the scale — Tailwind `sky-500`, the same hue `MatchRow` tints a
 * victory row with. Reusing the match feed's result axis is deliberate: the two
 * surfaces sit on the same page, so a blue square has to mean on the grid what
 * it means twenty pixels to the right.
 */
export const ACTIVITY_WIN_RGB = [14, 165, 233] as const

/**
 * Loss end of the scale — `rosegold-500`, the app primary (see
 * `assets/css/main.css`). The match feed uses plain red for a defeat, but a wall
 * of red squares reads as an error state rather than as data; the primary is a
 * warm rose that stays legible next to the sky without shouting.
 */
export const ACTIVITY_LOSS_RGB = [217, 115, 108] as const

/** Alpha of the least emphatic populated cell — a coin-flip, single-game period. */
export const ACTIVITY_MIN_ALPHA = 0.18

/** Alpha of the most emphatic cell — the busiest period, fully decided. */
export const ACTIVITY_MAX_ALPHA = 0.8

/**
 * Fill for one cell, or `null` when the cell has no games.
 *
 * Two channels, chosen so neither can mask the other:
 * - **hue** is which side of 50% the win rate sits on. A cell is blue-ish or
 *   rose-ish, never an interpolated mud in between, so the sign of the result is
 *   readable at 20 px.
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
 * Result line for a cell's tooltip. An empty period says so in words instead of
 * printing "0 games · 0%" — a fabricated rate is exactly what the empty state
 * exists to avoid.
 */
export function activityBucketResult(bucket: ActivityBucket): string {
  if (bucket.games <= 0 || bucket.winRate === null) return 'No games'

  const losses = bucket.games - bucket.wins
  const rate = `${Math.round(bucket.winRate * 100)}%`

  if (bucket.games === 1) return bucket.wins === 1 ? 'Victory' : 'Defeat'
  return `${bucket.wins}W – ${losses}L (${rate})`
}

/**
 * One line stating what the active mode actually covers, so a reader is never
 * left to assume that four modes of one grid describe the same population.
 *
 * The two cases are the retention asymmetry itself: the match-sourced modes can
 * only see the window `match_participants` still holds, while the patch mode
 * reads the frozen per-champion aggregate and therefore covers the whole career
 * — for one champion. Both facts come off the payload rather than being
 * hardcoded here, so the copy cannot drift from the data.
 */
export function activityCoverageNote(
  series: ActivitySeries,
  championName: string | null,
): string {
  if (series.source === 'aggregates') {
    if (series.buckets.length === 0) {
      return series.championId === null
        ? 'No champion is classified as a main yet, so there is no per-patch history to show.'
        : 'No aggregated patch history yet on the signature champion.'
    }
    const patches = series.buckets.length
    const who = championName ?? 'the signature champion'
    const label = patches === 1 ? 'patch' : 'patches'
    return `${who} only, across ${patches} ${label}. Aggregates are never pruned, so this is the whole tracked career — the other modes are all champions but stop at the retention window.`
  }

  if (series.buckets.length === 0) {
    return 'No games left in the retained window — match history is pruned after about two patches. Switch to Patch for the full career.'
  }

  const from = activityDayLabel(series.coverageFromUtc)
  const to = activityDayLabel(series.coverageToUtc)
  const span = from && to ? `${from} → ${to}` : 'the retained window'
  return `All champions, ${span}. Older games are pruned after about two patches, so this grid stops where the data does.`
}

/** `Jul 3` for an ISO instant, or `null` when there is none. */
export function activityDayLabel(iso: string | null): string | null {
  if (!iso) return null
  return new Date(iso).toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    timeZone: 'UTC',
  })
}
