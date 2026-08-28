import { isLoadingStatus } from '~/utils/async-data'

interface ChampionPatchSliceConfig<T> {
  /**
   * Fetch-key prefix, e.g. `champion-scaling`. Part of the shared Nuxt data
   * cache key, so it must stay stable per slice — changing it would orphan
   * every cached entry.
   */
  keyPrefix: string
  /** Path segment under `/api/champions/{id}/`, e.g. `scaling`. */
  endpoint: string
  /**
   * Empty read-model resolved instead of fetching while the request is gated
   * off (`enabled` false, or no position resolved yet).
   */
  emptyModel: (championId: number, position: string, patch: string | null) => T
}

/**
 * Factory for the patch-scoped champion detail slices (timeline leads /
 * scaling / powerspikes / roam). They all share the same contract:
 *
 * - keyed on (champion, position, patch, elo) so each slice dedupes with the
 *   rest of the detail page and re-fetches when any filter changes;
 * - `server: false`, mirroring `useChampion` (the detail page is
 *   client-rendered);
 * - `enabled` gates the request until the champion (and its lane) resolves,
 *   so the fetch holds instead of firing once with a null lane and again the
 *   moment it arrives.
 *
 * Each wrapper only supplies its key prefix, endpoint and empty model.
 *
 * Consumers should read the returned `pending`, not `status`: while the gate is
 * closed the handler resolves the empty model, so `status` reaches `success`
 * with nothing loaded and a chart driven off it flashes its "no data" state for
 * the whole champion fetch before filling in. `pending` folds the gate into the
 * loading flag once, here, so no page has to remember the trap (it deliberately
 * supersedes the `pending` Nuxt returns, which only knows about the request).
 */
export function createChampionPatchSlice<T>(config: ChampionPatchSliceConfig<T>) {
  return function useChampionPatchSlice(
    championId: MaybeRefOrGetter<number>,
    position: MaybeRefOrGetter<string | null | undefined>,
    patch: MaybeRefOrGetter<string | null | undefined>,
    enabled: MaybeRefOrGetter<boolean> = true,
    eloBracket: MaybeRefOrGetter<string | null | undefined> = undefined,
  ) {
    const championIdRef = computed(() => toValue(championId))
    const positionRef = computed(() => toValue(position) || undefined)
    const patchRef = computed(() => toValue(patch) || undefined)
    const enabledRef = computed(() => toValue(enabled))
    const eloBracketRef = computed(() => toValue(eloBracket) || undefined)

    const gated = computed(() => !enabledRef.value || !positionRef.value)

    const result = useLazyAsyncData<T>(
      () => `${config.keyPrefix}|${championIdRef.value}|${positionRef.value ?? ''}|${patchRef.value ?? ''}|${eloBracketRef.value ?? ''}`,
      () => {
        // `gated` already covers the missing lane; the second half of the
        // condition is what narrows it to a string for the query below.
        const resolvedPosition = positionRef.value
        if (gated.value || !resolvedPosition) {
          return Promise.resolve(config.emptyModel(
            championIdRef.value,
            resolvedPosition ?? '',
            patchRef.value ?? null,
          ))
        }
        const query: Record<string, string> = { position: resolvedPosition }
        if (patchRef.value) query.patch = patchRef.value
        if (eloBracketRef.value) query.eloBracket = eloBracketRef.value
        return $fetch<T>(
          `/api/champions/${championIdRef.value}/${config.endpoint}`,
          { query },
        )
      },
      { watch: [championIdRef, positionRef, patchRef, enabledRef, eloBracketRef], server: false },
    )

    // "Gate closed" is indistinguishable from "loaded and empty" in `status`:
    // the empty model resolves straight to `success`. Compose the two here so
    // a consumer can bind a skeleton to a single flag and never render an
    // answer nobody asked the API for.
    const pending = computed(() => gated.value || isLoadingStatus(result.status.value))

    return { ...result, pending }
  }
}
