/**
 * A headline count, rounded down to a short form: `490365` → `490k`,
 * `1204886` → `1.2M`. For the figures whose job is to say *how much* rather than
 * *exactly how much* — the homepage hero chips. A precise 490,365 invites the
 * reader to treat it as a live counter and to notice it moving; the site's real
 * claim is the order of magnitude.
 *
 * Always rounds **down**, never to nearest, so the number is a floor the data
 * genuinely clears — a stats site that promises honest sample sizes should not
 * round 999,600 up to "1M". One decimal below 10 (`4.1k`, `1.2M`) because
 * dropping it there loses a tenth of the value; none above it (`41k`, `490k`),
 * where it would only add noise. Under 1,000 the exact number is short enough to
 * print as is.
 *
 * Fixed `en-US` grouping: the homepage chip is server-rendered, so SSR and the
 * browser must format identically or hydration mismatches.
 */
export function formatCompactCount(value: number): string {
  if (!Number.isFinite(value) || value < 1000) {
    return Math.max(0, Math.floor(value || 0)).toLocaleString('en-US')
  }

  const [divisor, suffix] = value < 1_000_000 ? [1_000, 'k'] : [1_000_000, 'M']
  const scaled = value / divisor

  return scaled < 10
    ? `${(Math.floor(scaled * 10) / 10).toFixed(1)}${suffix}`
    : `${Math.floor(scaled)}${suffix}`
}
