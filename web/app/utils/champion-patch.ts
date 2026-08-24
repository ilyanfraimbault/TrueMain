/**
 * Which patch the champion detail pages pin their static bundles (rune tree,
 * items, summoner spells) to. Extracted from `useChampionDetailStatics` so the
 * fallback below is unit-testable: those bundles are fetched lazily and only
 * once this returns non-null, so a null it never leaves is not a missing
 * number on screen but a page stuck on its loading bar forever (#1211).
 *
 * Precedence:
 * 1. the loaded champion's own patch — what the page is actually showing;
 * 2. the URL filter, which stands in for it before the fetch lands;
 * 3. the latest DDragon version, but **only** once the champion fetch has
 *    settled.
 *
 * That last gate is the whole point. While the champion is still in flight a
 * real patch may yet arrive, and resolving to `latest` early would fetch the
 * (large) static payloads under one key and immediately refetch them under the
 * champion's — exactly the double round trip the deferred `immediate: false`
 * fetches exist to avoid (#817). Once the fetch has settled without a patch —
 * a champion we hold no aggregate for, on an unfiltered URL — no real patch is
 * coming, so falling back costs nothing and is the only thing that lets the
 * page render its no-build state.
 */
export function resolveChampionStaticPatch(input: {
  /** Patch of the loaded champion aggregate, if one loaded at all. */
  championPatch?: string | null
  /** Patch pinned by the URL filter. */
  filterPatch?: string | null
  /** Newest DDragon version, e.g. `15.16.1`. */
  latestVersion?: string | null
  /** Whether the champion fetch has settled (success or error). */
  championSettled: boolean
}): string | null {
  return input.championPatch
    || input.filterPatch
    || (input.championSettled ? input.latestVersion : null)
    || null
}
