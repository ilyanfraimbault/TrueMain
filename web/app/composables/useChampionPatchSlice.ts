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

    return useLazyAsyncData<T>(
      () => `${config.keyPrefix}|${championIdRef.value}|${positionRef.value ?? ''}|${patchRef.value ?? ''}|${eloBracketRef.value ?? ''}`,
      () => {
        if (!enabledRef.value || !positionRef.value) {
          return Promise.resolve(config.emptyModel(
            championIdRef.value,
            positionRef.value ?? '',
            patchRef.value ?? null,
          ))
        }
        const query: Record<string, string> = { position: positionRef.value }
        if (patchRef.value) query.patch = patchRef.value
        if (eloBracketRef.value) query.eloBracket = eloBracketRef.value
        return $fetch<T>(
          `/api/champions/${championIdRef.value}/${config.endpoint}`,
          { query },
        )
      },
      { watch: [championIdRef, positionRef, patchRef, enabledRef, eloBracketRef], server: false },
    )
  }
}
