import type { ChampionScalingResponse } from '#shared/contract/api'
import { isLoadingStatus } from '#common/utils/async-data'

/**
 * Win rate by game duration for the champion page (#537): patch-scoped, keyed on
 * (champion, position, patch, rank) so it re-fetches when any filter changes,
 * and client-only like the champion read.
 *
 * `enabled` holds the request until the champion's lane resolves, so it fires
 * once with the lane instead of once with none and again when it lands. Bind
 * skeletons to the returned `pending`, not `status`: while the gate is shut the
 * handler resolves an empty model straight to `success`, so `status` alone would
 * flash the chart's no-data state for the whole champion fetch.
 */
export function useChampionScaling(
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

  const apiFetch = useApiFetch()

  const result = useLazyAsyncData<ChampionScalingResponse>(
    () => `champion-scaling|${championIdRef.value}|${positionRef.value ?? ''}|${patchRef.value ?? ''}|${eloBracketRef.value ?? ''}`,
    (_nuxtApp, { signal }) => {
      const resolvedPosition = positionRef.value
      if (gated.value || !resolvedPosition) {
        return Promise.resolve({
          championId: championIdRef.value,
          position: resolvedPosition ?? '',
          patch: patchRef.value ?? null,
          buckets: [],
          scalingIndex: null,
        })
      }
      const query: Record<string, string> = { position: resolvedPosition }
      if (patchRef.value) query.patch = patchRef.value
      if (eloBracketRef.value) query.eloBracket = eloBracketRef.value
      return apiFetch<ChampionScalingResponse>(`/champions/${championIdRef.value}/scaling`, { query, signal })
    },
    { watch: [championIdRef, positionRef, patchRef, enabledRef, eloBracketRef], server: false },
  )

  const pending = computed(() => gated.value || isLoadingStatus(result.status.value))

  return { ...result, pending }
}
