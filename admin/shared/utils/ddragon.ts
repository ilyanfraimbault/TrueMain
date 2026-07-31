/**
 * Data Dragon lists one entry per playable champion *kit*, not per champion.
 * Patch 16.15 ("League classique") added 60 legacy kits reusing the original
 * champion's display name — `Jade_Ahri` with key `60103` next to Ahri's `103`.
 * The ingestor only aggregates queue 420, so those ids never carry a single
 * stat and would only duplicate every row of the champion list.
 *
 * Real Riot champion keys are still well under this floor (Naafiri, the
 * highest, is 950) and every alternate-mode kit so far sits at
 * `60000 + <base key>`, so cutting here keeps an order of magnitude of headroom
 * for real champions while catching a future mode built the same way.
 */
export const ALTERNATE_MODE_CHAMPION_ID_FLOOR = 10_000

export function isLiveChampionId(championId: number): boolean {
  return Number.isFinite(championId) && championId > 0 && championId < ALTERNATE_MODE_CHAMPION_ID_FLOOR
}

export function normalizeDataDragonPatch(patch?: string | null): string | null {
  if (!patch) {
    return null
  }

  const segments = patch.split('.').filter(Boolean)
  if (segments.length === 2) {
    return `${segments[0]}.${segments[1]}.1`
  }

  return patch
}
