import type { RankHistoryEntry } from '#shared/types/rank-history'
import { rankScore, tierHex } from '#common/utils/tiers'

// The ranked card's LP curve (`profile/ProfileRankChart.vue`) and its LP delta
// chips, as pure functions over the rank-history snapshots.

const DAY_MS = 24 * 60 * 60 * 1000

// ─── Chart series ─────────────────────────────────────────────────────────
// A single continuous series: the rank score at each snapshot.
//
// The tier is deliberately *not* modelled as one series per tier. Doing that
// gives every tier its own area path anchored at y = 0, and because the
// y-domain floats far above zero (a Challenger sits around 5500), the
// out-of-run zeros drag each path down through the entire plot. Every
// promotion or demotion then paints a full-height wedge where two tier areas
// overlap — the "triangles" the card used to show around short tier dips.
//
// The tier colour lives on an x-axis gradient instead, with one hard-edged
// stop pair per tier run (see `tierGradientStops`), painted onto both the line
// and the fill so the whole chart colour-shifts at each transition without
// ever splitting into per-tier paths.
export interface RankChartPoint extends Record<string, unknown> {
  entry: RankHistoryEntry
  score: number
}

export interface GradientStop {
  offset: string
  color: string
}

/** A tier crest on the chart's y-axis, pinned at that tier's median score. */
export interface CrestBand {
  tier: string
  score: number
}

export function rankChartPoints(history: readonly RankHistoryEntry[]): RankChartPoint[] {
  return history.map(entry => ({
    entry,
    score: rankScore(entry.tier, entry.division, entry.leaguePoints),
  }))
}

// One stop pair per contiguous tier run, positioned on the *midpoint*
// between it and each neighbouring run rather than on its own end points.
// Two consecutive runs then share their boundary stop at the exact same
// offset — an SVG hard stop, an instant colour change with no interpolated
// band — instead of each ending on its own last/first point and leaving a
// gap the gradient blends across. This also matters for a run that is a
// single snapshot: without the half-point extension toward each neighbour,
// its own start and end stops coincide and it never gets a visible band at
// all, just the blend from its neighbours fading through where it sat.
export function tierGradientStops(points: readonly RankChartPoint[]): GradientStop[] {
  const last = points.length - 1
  if (last < 1) return []

  const stops: GradientStop[] = []
  let runStart = 0
  for (let i = 1; i <= last + 1; i++) {
    const runTier = points[runStart]!.entry.tier
    const sameRun = i <= last && points[i]!.entry.tier.toUpperCase() === runTier.toUpperCase()
    if (sameRun) continue
    const runEnd = i - 1
    const color = tierHex(runTier)
    const left = runStart === 0 ? 0 : (runStart - 0.5) / last * 100
    const right = runEnd === last ? 100 : (runEnd + 0.5) / last * 100
    stops.push({ offset: `${left.toFixed(3)}%`, color })
    stops.push({ offset: `${right.toFixed(3)}%`, color })
    runStart = i
  }
  return stops
}

// An objectBoundingBox gradient needs a non-degenerate box: a flat history
// gives the line path zero height and the browser drops the element
// altogether, so single-tier and dead-flat histories keep a plain colour.
export function lineStrokeOf(points: readonly RankChartPoint[], currentTier: string | null, gradientUrl: string): string {
  const scores = points.map(p => p.score)
  const distinctTiers = new Set(points.map(p => p.entry.tier.toUpperCase())).size
  if (distinctTiers < 2 || Math.min(...scores) === Math.max(...scores)) {
    return tierHex(currentTier)
  }
  return gradientUrl
}

// The area has two degenerate cases of its own:
//  - A single-point history leaves `tierGradientStops` empty (see its
//    `last < 1` guard), so the gradient it would feed has no `<stop>` children
//    and resolves to `none` — an invisible fill.
//  - A flat history pinned at score 0 (Iron IV 0 LP, reachable — see
//    `rankScore`'s docstring — by an inactive account whose whole tracked
//    window sits at the ladder floor) hits the same zero-height
//    bounding-box case as the line, but for a different reason: the area's
//    baseline defaults to the data value 0 (`Area`'s `baseline: () => 0` in
//    `@unovis/ts`), so with every point *also* at 0 its top and bottom
//    edges coincide exactly, regardless of the y-domain padding.
export function areaFillOf(points: readonly RankChartPoint[], currentTier: string | null, gradientUrl: string): string {
  if (tierGradientStops(points).length === 0) return tierHex(currentTier)
  const scores = points.map(p => p.score)
  if (Math.min(...scores) === 0 && Math.max(...scores) === 0) return tierHex(currentTier)
  return gradientUrl
}

// Pad the Y range by 25% (min 50 LP-equivalents) so the line never hugs
// the top/bottom edge. Falls back to [0, 400] (Iron band) when there's
// nothing to plot so the empty chart has a sensible scale.
export function rankChartYDomain(points: readonly RankChartPoint[]): [number, number] {
  if (points.length === 0) return [0, 400]
  const scores = points.map(p => p.score)
  const minScore = Math.min(...scores)
  const maxScore = Math.max(...scores)
  const padded = Math.max(50, (maxScore - minScore) * 0.25)
  return [Math.max(0, minScore - padded), maxScore + padded]
}

// Tier crests rendered alongside the chart. One crest per *tier* present
// in the visible history — division changes within the same tier
// (Emerald IV → Emerald II, etc.) don't add a crest. Each crest is pinned
// at the median score of that tier's run so apex bands (Master, GM,
// Challenger) — which share a single tier floor — still stack vertically
// in climb order instead of collapsing to the same Y.
export function crestBands(points: readonly RankChartPoint[]): CrestBand[] {
  const buckets = new Map<string, number[]>()
  for (const point of points) {
    const tier = point.entry.tier.toUpperCase()
    const arr = buckets.get(tier)
    if (arr) arr.push(point.score)
    else buckets.set(tier, [point.score])
  }
  return Array.from(buckets, ([tier, scores]) => {
    const sorted = scores.slice().sort((a, b) => a - b)
    const median = sorted[Math.floor(sorted.length / 2)]!
    return { tier, score: median }
  }).sort((a, b) => a.score - b.score)
}

/** The pixel offset from the chart's top of a score, for a chart `height` px tall. */
export function scoreTopPx(score: number, [yMin, yMax]: [number, number], height: number): number {
  if (yMax === yMin) return height / 2
  const ratio = (score - yMin) / (yMax - yMin)
  return height * (1 - ratio)
}

export function rankChartDateLabel(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
  })
}

// ─── LP deltas ────────────────────────────────────────────────────────────
// "Last 7d / 30d" chips compare the latest rank score to the last snapshot
// at-or-before the cutoff, falling back to the earliest tracked snapshot
// when the player has no older data. Returns null when there's no
// meaningful comparison (≤1 snapshot, or the cutoff snapshot is the
// current one).
export function lpDeltaSince(points: readonly RankChartPoint[], days: number, now: number = Date.now()): number | null {
  if (points.length < 2) return null
  const cutoff = now - days * DAY_MS
  let base: RankChartPoint | undefined
  for (const point of points) {
    const t = new Date(point.entry.capturedAtUtc).getTime()
    if (t <= cutoff) base = point
    else break
  }
  base ??= points[0]
  const current = points[points.length - 1]
  if (!base || !current || base === current) return null
  return current.score - base.score
}
