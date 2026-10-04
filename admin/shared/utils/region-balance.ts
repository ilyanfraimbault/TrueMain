import type { PlatformBalance, RegionBalance } from '~~/shared/types/ops'

/**
 * Presentation rules for the health cockpit's region-balance panel (#1153). Under `shared/`
 * so they can be unit-tested — the admin suite has no page-level harness.
 */

/** One chart row: the UTC day plus one count per platform. */
export type RegionDailyRow = { label: string } & Record<string, number | string>

/**
 * The window's UTC days, oldest first, as `YYYY-MM-DD`. Built from `windowStartUtc` and
 * `windowDays` rather than from the rows, because a day no platform ingested on has no row
 * and must still show up — as a gap in the bars, which is the drift this chart exists for.
 */
export function windowDays(balance: Pick<RegionBalance, 'windowStartUtc' | 'windowDays'>): string[] {
  const start = new Date(balance.windowStartUtc)
  if (Number.isNaN(start.getTime()) || balance.windowDays <= 0) {
    return []
  }
  return Array.from({ length: balance.windowDays }, (_, index) => {
    const day = new Date(start.getTime() + index * 86_400_000)
    return day.toISOString().slice(0, 10)
  })
}

/**
 * Pivots the per-(day, platform) rows into one chart row per window day. A platform with no
 * row on a day ingested nothing that day: the query covers the whole window, so the zero is
 * measured, not assumed.
 */
export function regionDailyRows(balance: RegionBalance, platforms: string[]): RegionDailyRow[] {
  const counts = new Map<string, number>()
  for (const row of balance.dailyMatches) {
    counts.set(`${row.day}|${row.platformId}`, (counts.get(`${row.day}|${row.platformId}`) ?? 0) + row.matches)
  }
  return windowDays(balance).map((day) => {
    const row: RegionDailyRow = { label: day }
    for (const platform of platforms) {
      row[platform] = counts.get(`${day}|${platform}`) ?? 0
    }
    return row
  })
}

/**
 * The platforms worth a chart series: the ones in the claim, plus any other that ingested
 * something in the window (a platform dropped from the claim mid-window still has bars).
 */
export function chartedPlatforms(balance: RegionBalance): string[] {
  const ingested = new Set(balance.dailyMatches.filter(row => row.matches > 0).map(row => row.platformId))
  return balance.platforms
    .filter(platform => platform.inClaim !== false || ingested.has(platform.platformId))
    .map(platform => platform.platformId)
}

/** How a platform's claim membership reads in the table. */
export function claimLabel(platform: Pick<PlatformBalance, 'inClaim'>): string {
  if (platform.inClaim === null) {
    return 'unknown'
  }
  return platform.inClaim ? 'in claim' : 'not claimed'
}
