import type { RankHistoryEntry } from '#shared/types/rank-history'
import { rankScore } from '#common/utils/tiers'

// The ranked card's LP curve (`profile/ProfileRankChart.vue`) and its LP delta
// chips, as pure functions over the rank-history snapshots.

const DAY_MS = 24 * 60 * 60 * 1000

// ─── Chart series ─────────────────────────────────────────────────────────
// A single continuous series: the rank score at each snapshot. The tier colour
// is not the snapshot's: the curve is clipped by the tier bands it crosses
// (`rank-chart-zones.ts`), so a rank change shows where the line passes a
// threshold rather than at the snapshot whose tier changed.
export interface RankChartPoint extends Record<string, unknown> {
  entry: RankHistoryEntry
  score: number
}

export function rankChartPoints(history: readonly RankHistoryEntry[]): RankChartPoint[] {
  return history.map(entry => ({
    entry,
    score: rankScore(entry.tier, entry.division, entry.leaguePoints),
  }))
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
