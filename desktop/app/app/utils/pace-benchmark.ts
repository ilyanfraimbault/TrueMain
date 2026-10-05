/**
 * The pace benchmark (#1912): what laners of a tier had at each minute, read
 * from `GET /benchmarks/pace?position=` and set against the player's own pace
 * on the overlay. The API answers every tier of a position; the tier is picked
 * here, on the player's machine, so the rank read from the client is never
 * sent (#1805).
 */

/** Mirrors `PaceBenchmarkResponse` in `Api/ReadModels/Benchmarks`. */
export interface PaceBenchmarkResponse {
  position: string
  patches: string[]
  minSamples: number
  tiers: { tier: string, minutes: PaceBenchmarkMinute[] }[]
}

export interface PaceBenchmarkMinute {
  minute: number
  samples: number
  /** Cumulative; null under the sample floor. */
  cs: PaceQuartiles | null
  goldEarned: PaceQuartiles | null
}

export interface PaceQuartiles {
  p25: number
  median: number
  p75: number
}

/** Where the player sits against the tier's quartiles: under the first, between, over the third. */
export type PaceStanding = 'below' | 'within' | 'above'

/** One metric of the reference at the current minute, as a per-minute rate. */
export interface PaceComparison {
  median: number
  standing: PaceStanding
}

/** What the stats panel draws beside the player's own pace. */
export interface PaceReference {
  /** The tier, as the client spells it (`DIAMOND`). */
  tier: string
  cs: PaceComparison | null
  gold: PaceComparison | null
  /** The tier's median CS per minute at each minute it is served, for the curve. */
  csCurve: { minute: number, value: number }[]
}

/** The tier name as a reader writes it: `GRANDMASTER` → `Grandmaster`. */
export function tierLabel(tier: string): string {
  return tier.charAt(0) + tier.slice(1).toLowerCase()
}

function standing(value: number, quartiles: PaceQuartiles): PaceStanding {
  if (value < quartiles.p25) return 'below'
  if (value > quartiles.p75) return 'above'
  return 'within'
}

function compare(value: number, minute: number, quartiles: PaceQuartiles | null): PaceComparison | null {
  if (!quartiles || minute <= 0) return null
  return { median: quartiles.median / minute, standing: standing(value, quartiles) }
}

/**
 * The reference for a player of `tier` at `minute`, whose cumulative CS and
 * gold are `cs` and `gold`. Null without a ranked Solo/Duo tier, or when the
 * tier has no data at this position; a metric is null at a minute under the
 * sample floor, so the panel shows no comparison rather than a thin one.
 */
export function paceReference(
  benchmark: PaceBenchmarkResponse | null,
  tier: string | null,
  sample: { minute: number, cs: number, gold: number } | null,
): PaceReference | null {
  if (!benchmark || !tier || !sample) return null
  const row = benchmark.tiers.find(entry => entry.tier === tier.toUpperCase())
  if (!row) return null
  const at = row.minutes.find(entry => entry.minute === sample.minute) ?? null
  return {
    tier: row.tier,
    cs: compare(sample.cs, sample.minute, at?.cs ?? null),
    gold: compare(sample.gold, sample.minute, at?.goldEarned ?? null),
    csCurve: row.minutes
      .filter(entry => entry.cs !== null)
      .map(entry => ({ minute: entry.minute, value: entry.cs!.median / entry.minute })),
  }
}
