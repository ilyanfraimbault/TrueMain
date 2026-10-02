/**
 * The pages the usage counts name (#1805), by the route they are on. The
 * shell counts them (`src-tauri/src/telemetry.rs`) and the API keeps only the
 * keys its catalog lists (`DesktopTelemetryCatalog.cs`): a page added here is
 * added there too, or it is never counted. The overlay panels and the `/dev`
 * tools are not pages a player opens, so they have no key.
 */
const PAGE_KEYS: Record<string, string> = {
  'index': 'dashboard',
  'champions': 'champions',
  'champions-id': 'champion',
  'tierlist': 'tierlist',
  'matchup': 'matchup',
  'truemains': 'truemains',
  'favorites': 'favorites',
  'draft': 'draft',
  'game': 'game',
  'recordings': 'recordings',
  'recordings-id': 'recording',
  'recordings-clips-id': 'clip',
}

/** The key a route is counted under, or none. */
export function telemetryPage(routeName: unknown): string | null {
  return typeof routeName === 'string' ? PAGE_KEYS[routeName] ?? null : null
}
