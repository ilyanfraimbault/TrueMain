import type { ApexCutoff } from '#shared/types/apex-cutoffs'
import type { RankChartPoint } from '#common/utils/rank-chart'
import { ELO_TIERS } from '#common/utils/elo-brackets'

// Where each tier starts on the LP curve's score axis (`rankScore`), so the
// chart can draw a rank change where it happens: when the curve crosses a
// threshold, i.e. horizontally, not at the snapshot whose tier changed.
//
// Below Master a tier starts at a fixed score (400 per tier). Master starts at
// 2800 too, but Grandmaster and Challenger have no fixed LP: each region
// promotes whoever sits above the lowest LP of its Grandmaster / Challenger
// league that day, so those two thresholds move from one snapshot to the next
// and are only known where a cutoff was measured (`ApexCutoff`).

const DAY_MS = 24 * 60 * 60 * 1000
const TIER_SPAN = 400
export const MASTER_FLOOR = ELO_TIERS.indexOf('MASTER') * TIER_SPAN

// A cutoff is read every few hours; a snapshot further than this from the
// latest one draws no apex line rather than a stale one.
const MAX_HOLD_DAYS = 2

// The next threshold above the curve's peak joins the y-range when it is this
// close, so a Master 40 LP under Grandmaster sees the line it is chasing.
const NEAR_THRESHOLD = 150

/**
 * The Grandmaster and Challenger floors, as rank scores, at one snapshot: the
 * cutoff of its day (or held from a recent one), else `null`. `nearest*` is the
 * closest cutoff of the history in either direction, however far: no line is
 * drawn from it, but the band it bounds is, so an apex history with any cutoff
 * at all never falls back to splitting at the snapshot whose tier changed.
 */
export interface ApexFloors {
  grandmaster: number | null
  challenger: number | null
  nearestGrandmaster?: number | null
  nearestChallenger?: number | null
}

/**
 * Where one tier starts at each snapshot. `measured` is false where the
 * threshold is unknown (no apex cutoff on or shortly before that day): the value
 * is then the history's nearest cutoff, or with none at all the snapshot's own
 * tier read back as a floor — under the score when the player held that tier,
 * out of reach when not — so the colour stays right without drawing a line the
 * data doesn't have.
 */
export interface TierBoundary {
  tier: string
  scores: number[]
  measured: boolean[]
}

/** One tier's band: between its own boundary and the next tier's. */
export interface TierZone {
  tier: string
  lower: TierBoundary | null
  upper: TierBoundary | null
}

function utcDay(iso: string): number {
  const date = new Date(iso)
  return Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate())
}

function heldValue(
  cutoffs: readonly { day: number, value: number | null }[],
  day: number,
): number | null {
  let latest: { day: number, value: number } | null = null
  for (const cutoff of cutoffs) {
    if (cutoff.day > day) break
    if (cutoff.value !== null) latest = { day: cutoff.day, value: cutoff.value }
  }
  if (!latest || day - latest.day > MAX_HOLD_DAYS * DAY_MS) return null
  return MASTER_FLOOR + latest.value
}

function nearestValue(
  cutoffs: readonly { day: number, value: number | null }[],
  day: number,
): number | null {
  let best: { distance: number, value: number } | null = null
  for (const cutoff of cutoffs) {
    if (cutoff.value === null) continue
    const distance = Math.abs(cutoff.day - day)
    if (!best || distance < best.distance) best = { distance, value: cutoff.value }
  }
  return best ? MASTER_FLOOR + best.value : null
}

/** The apex floors in effect on each snapshot's UTC day. */
export function apexFloorsAt(points: readonly RankChartPoint[], cutoffs: readonly ApexCutoff[]): ApexFloors[] {
  const byDay = cutoffs
    .map(cutoff => ({ day: utcDay(cutoff.dateUtc), gm: cutoff.grandmasterMinLp, ch: cutoff.challengerMinLp }))
    .sort((a, b) => a.day - b.day)
  const gm = byDay.map(c => ({ day: c.day, value: c.gm }))
  const ch = byDay.map(c => ({ day: c.day, value: c.ch }))
  return points.map((point) => {
    const day = utcDay(point.entry.capturedAtUtc)
    return {
      grandmaster: heldValue(gm, day),
      challenger: heldValue(ch, day),
      nearestGrandmaster: nearestValue(gm, day),
      nearestChallenger: nearestValue(ch, day),
    }
  })
}

const tierRank = (tier: string) => ELO_TIERS.indexOf(tier.toUpperCase() as typeof ELO_TIERS[number])

/** Every tier's starting line, Bronze to Challenger, along the snapshots. */
export function tierBoundaries(points: readonly RankChartPoint[], floors: readonly ApexFloors[]): TierBoundary[] {
  const fixed = ELO_TIERS.slice(1, tierRank('MASTER') + 1).map((tier, i) => ({
    tier,
    scores: points.map(() => (i + 1) * TIER_SPAN),
    measured: points.map(() => true),
  }))

  const gmRank = tierRank('GRANDMASTER')
  const grandmaster: TierBoundary = {
    tier: 'GRANDMASTER',
    scores: points.map((point, i) =>
      floors[i]?.grandmaster
      ?? floors[i]?.nearestGrandmaster
      ?? (tierRank(point.entry.tier) >= gmRank ? MASTER_FLOOR : Infinity)),
    measured: points.map((_, i) => floors[i]?.grandmaster != null),
  }
  const challenger: TierBoundary = {
    tier: 'CHALLENGER',
    scores: points.map((point, i) => Math.max(
      grandmaster.scores[i]!,
      floors[i]?.challenger
      ?? floors[i]?.nearestChallenger
      ?? (point.entry.tier.toUpperCase() === 'CHALLENGER' ? MASTER_FLOOR : Infinity),
    )),
    measured: points.map((_, i) => floors[i]?.challenger != null),
  }
  return [...fixed, grandmaster, challenger]
}

/** The tiers as bands between consecutive boundaries, Iron to Challenger. */
export function tierZones(boundaries: readonly TierBoundary[]): TierZone[] {
  return ELO_TIERS.map((tier, i) => ({
    tier,
    lower: i === 0 ? null : boundaries[i - 1] ?? null,
    upper: boundaries[i] ?? null,
  }))
}

// Pad the Y range by 25% (min 50 LP-equivalents) so the line never hugs the
// top/bottom edge, then stretch it to the next threshold above when that one is
// close. Falls back to [0, 400] (Iron band) when there's nothing to plot so the
// empty chart has a sensible scale.
export function rankChartYDomain(
  points: readonly RankChartPoint[],
  boundaries: readonly TierBoundary[] = [],
): [number, number] {
  if (points.length === 0) return [0, 400]
  const scores = points.map(p => p.score)
  const minScore = Math.min(...scores)
  const maxScore = Math.max(...scores)
  const padded = Math.max(50, (maxScore - minScore) * 0.25)
  let top = maxScore + padded

  const above = boundaries
    .flatMap(b => b.scores.filter((score, i) => b.measured[i] && score > maxScore))
  const next = above.length > 0 ? Math.min(...above) : null
  if (next !== null && next - maxScore <= NEAR_THRESHOLD) top = Math.max(top, next + 25)

  return [Math.max(0, minScore - padded), top]
}
