import type { ChampionSummaryResponse } from '../types/champions'

/**
 * Freshness dates for the sitemap's champion URLs (#1256).
 *
 * Pure and shared so the rule the sitemap advertises is pinned by tests rather
 * than by reading the route — a `lastmod` is a claim made to a crawler, and a
 * wrong one is worse than an absent one.
 */

/**
 * An ISO instant, truncated to its UTC day (`2026-08-26`), or null when the
 * value is missing, blank or unparseable.
 *
 * **Day precision is the point, not a shortcut.** `lastUpdatedAtUtc` tracks the
 * incremental fold and moves every few minutes while it runs, so a
 * minute-precision `lastmod` would tell a crawler that all ~174 champion pages
 * changed within the hour, every hour — which is how a sitemap teaches Google
 * to ignore the field. A win rate moving 51.2% → 51.3% is not a content change;
 * the day is the granularity at which these pages meaningfully move, and
 * `YYYY-MM-DD` is a valid W3C Datetime, which is what the sitemap spec asks for.
 */
export function toSitemapDay(value: string | null | undefined): string | null {
  if (!value) return null
  const parsed = new Date(value)
  const time = parsed.getTime()
  if (Number.isNaN(time)) return null
  return parsed.toISOString().slice(0, 10)
}

/**
 * `championId → lastmod day`, from the champion directory.
 *
 * The directory holds a row per `(champion, lane)`, so a flex pick has several;
 * the champion's page shows all of them, so its freshness is the **most recent**
 * of them. Champions the directory does not mention — every champion in the
 * days after a patch flip, before its lane is folded — are simply absent, and
 * the caller emits their URL with no `lastmod` rather than inventing one.
 */
export function championLastmodById(
  summaries: ChampionSummaryResponse[] | null | undefined,
): Map<number, string> {
  const latest = new Map<number, number>()

  // `Array.isArray`, not just a null check: a 200 carrying a malformed body
  // would make the loop below throw, and that throw escapes `championUrls` into
  // `loadSitemapUrls`'s catch — which would drop **every champion URL** rather
  // than just the dates. The contract is that the directory decorates the
  // sitemap and never decides it, so a contract violation has to fail here.
  if (!Array.isArray(summaries)) return latestToDays(latest)

  for (const row of summaries) {
    // Optional chaining for the same reason as the `Array.isArray` above, one
    // level down: a null or non-object element would throw on property access,
    // and that throw costs every champion URL rather than one date.
    const time = new Date(row?.lastUpdatedAtUtc ?? '').getTime()
    if (Number.isNaN(time)) continue
    const championId = row?.championId
    if (typeof championId !== 'number' || !Number.isFinite(championId)) continue
    const known = latest.get(championId)
    if (known === undefined || time > known) latest.set(championId, time)
  }

  return latestToDays(latest)
}

/**
 * Truncate only once the maximum is settled: comparing day strings would tie
 * every row folded on the same day and pick whichever happened to be last.
 */
function latestToDays(latest: Map<number, number>): Map<number, string> {
  const days = new Map<number, string>()
  for (const [championId, time] of latest) {
    const day = toSitemapDay(new Date(time).toISOString())
    if (day) days.set(championId, day)
  }
  return days
}
