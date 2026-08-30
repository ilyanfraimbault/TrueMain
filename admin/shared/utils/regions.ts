/**
 * The Riot platforms the pipeline actively tracks, written once for the admin.
 *
 * Same set as the Ingestor's `Platforms:Active` (`backend/Ingestor/
 * appsettings.json`) and the `TrackedPlatforms` guard in
 * `Api/Services/Ops/SeedRequestService.cs`. The Ingestor config stays the
 * source of truth for what the pipeline crawls — this is the admin's copy of
 * it, and the `/configuration` page renders the effective config next to it, so
 * a drift is visible rather than silent.
 *
 * Not derived at runtime from `GET /ops/configuration` on purpose: these values
 * populate `<USelect>` options that must exist before any ops call resolves
 * (an empty region select makes the seed form unusable if that one request is
 * slow or fails), they carry a compile-time `TrackedRegion` type that a runtime
 * array cannot, and the config array's order is a deployment detail, not a UI
 * ordering.
 *
 * Adding a shard is therefore two edits — the Ingestor config and this file —
 * where it used to be four, two of them inside the admin alone.
 */

/** Platform ids the pipeline crawls, in the order the admin selects show them. */
export const TRACKED_REGIONS = ['EUW1', 'KR', 'NA1'] as const

export type TrackedRegion = (typeof TRACKED_REGIONS)[number]

/**
 * `<USelect>` options for the tracked platforms. `value` is widened to `string`
 * because most consumers bind a free-form model (the single-add form's region,
 * the filter selects' `'all'` sentinel); callers that need the narrow type map
 * over `TRACKED_REGIONS` themselves.
 */
export const TRACKED_REGION_ITEMS: { label: string, value: string }[] = TRACKED_REGIONS.map(
  region => ({ label: region, value: region }),
)

/** The tracked set as a single operator-facing line, e.g. `EUW1 · KR · NA1`. */
export const TRACKED_REGIONS_LABEL = TRACKED_REGIONS.join(' · ')

/**
 * Matches a token against the tracked set, case-insensitively and
 * whitespace-tolerantly; null when it is not one of them. Callers treat null as
 * a hard error rather than falling back to a default — silently seeding the
 * wrong shard is worse than refusing the line.
 */
export function parseTrackedRegion(token: string | null | undefined): TrackedRegion | null {
  if (!token) {
    return null
  }
  const normalized = token.trim().toUpperCase()
  return TRACKED_REGIONS.find(region => region === normalized) ?? null
}
