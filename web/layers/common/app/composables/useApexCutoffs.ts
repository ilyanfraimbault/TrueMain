import type { ApexCutoff, ApexCutoffsResponse } from '#shared/types/apex-cutoffs'

/**
 * The Grandmaster / Challenger cutoffs of a platform over the rank-history
 * window, for the ranked card's LP curve (#2174). The same for every viewer of
 * a region, but only the client draws the curve, so it is fetched there. Any
 * failure, or no platform yet, reads as no cutoff: the curve then draws no
 * apex line rather than failing.
 */
export function useApexCutoffs(
  platformId: MaybeRefOrGetter<string | null | undefined>,
  days = 90,
) {
  const apiFetch = useApiFetch()
  const platform = computed(() => toValue(platformId) || null)

  const { data } = useLazyAsyncData<ApexCutoff[]>(
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

  return data
}
