import type { RankHistoryResponse } from '~~/shared/types/rank-history'

/**
 * Fetch the rank-history payload for the given <c>nameTag</c>. Mirrors the
 * shape of <c>useTruemainProfile</c> — 404 surfaces as <c>notFound</c>
 * instead of throwing, and the result is fetched client-side so different
 * viewers never share an SSR cache entry.
 */
export function useTruemainRankHistory(
  nameTag: MaybeRefOrGetter<string>,
  options: { days?: MaybeRefOrGetter<number> } = {},
) {
  const daysRef = computed(() => toValue(options.days) ?? 90)

  const data = ref<RankHistoryResponse | null>(null)

  const { isLoading, isInitialLoading, notFound, error } = useTruemainFetch<RankHistoryResponse>(nameTag, {
    watch: [daysRef],
    request: tag => $fetch<RankHistoryResponse | null>(
      `/api/truemains/${encodeURIComponent(tag)}/rank-history`,
      {
        query: { days: daysRef.value },
        ignoreResponseError: true,
      },
    ),
    validate: (response): response is RankHistoryResponse =>
      Boolean(response && typeof response === 'object' && Array.isArray(response.entries)),
    onResponse: (response) => { data.value = response },
    onClear: () => { data.value = null },
  })

  return {
    data,
    isLoading,
    isInitialLoading,
    notFound,
    error,
  }
}
