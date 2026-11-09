/**
 * The pages the usage counts name (#1805), by the route they are on. The API
 * keeps only the keys its catalog lists (`DesktopTelemetryCatalog.cs`), which
 * `tests/telemetry.test.ts` checks. The overlay panels and the `/dev` tools are
 * not pages a player opens, so they have no key.
 */
export const PAGE_KEYS: Readonly<Record<string, string>> = {
  'index': 'dashboard',
  'champions': 'champions',
  'champions-id': 'champion',
  'tierlist': 'tierlist',
  'matchup': 'matchup',
  'truemains': 'truemains',
  'favorites': 'favorites',
  'draft': 'draft',
  'game': 'game',
  'overlay': 'overlay',
  'recordings': 'recordings',
  'recordings-id': 'recording',
  'recordings-clips-id': 'clip',
}

/** The key a route is counted under, or none. */
export function telemetryPage(routeName: unknown): string | null {
  return typeof routeName === 'string' ? PAGE_KEYS[routeName] ?? null : null
}
