import type { ApexCutoff, ApexCutoffsResponse } from '#shared/types/apex-cutoffs'

/**
 * The Grandmaster / Challenger cutoffs of a platform over the rank-history
 * window, for the ranked card's LP curve (#2174). The same for every viewer of
 * a region, but only the client draws the curve, so it is fetched there. Any
 * failure reads as no cutoff: the curve then draws no apex line rather than
 * failing.
 *
 * `loading` holds from the moment the platform is known until its cutoffs have
 * answered (#2187): the card keeps its skeleton meanwhile, or the curve would
 * draw once without them — split at the snapshots whose tier changed — and
 * redraw half a second later. With no platform at all there is nothing to wait
 * for, and the curve draws without cutoffs.
 */
export function useApexCutoffs(
  platformId: MaybeRefOrGetter<string | null | undefined>,
  days = 90,
) {
  const apiFetch = useApiFetch()
  const platform = computed(() => toValue(platformId) || null)

  const { data, status } = useLazyAsyncData<ApexCutoff[]>(
    () => `apex-cutoffs-${platform.value ?? 'none'}-${days}`,
    async () => {
      if (!platform.value) return []
      const response = await apiFetch<ApexCutoffsResponse | null>(
        `/ladder/${encodeURIComponent(platform.value)}/apex-cutoffs`,
        { query: { days }, ignoreResponseError: true },
      ).catch(() => null)
      return Array.isArray(response?.entries) ? response.entries : []
    },
    { server: false, default: () => [] },
  )

  const loading = computed(() => platform.value !== null && status.value !== 'success' && status.value !== 'error')

  // One reactive object, so a template reads `apexCutoffs.cutoffs` unwrapped.
  return reactive({ cutoffs: data, loading })
}
