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
  const nameTagRef = computed(() => toValue(nameTag))
  const daysRef = computed(() => toValue(options.days) ?? 90)

  const data = ref<RankHistoryResponse | null>(null)
  const isLoading = ref(false)
  const isInitialLoading = ref(true)
  const notFound = ref(false)
  const error = ref<unknown>(null)

  async function fetchHistory() {
    if (!nameTagRef.value) {
      data.value = null
      notFound.value = false
      isInitialLoading.value = false
      return
    }

    isLoading.value = true
    error.value = null
    try {
      const response = await $fetch<RankHistoryResponse | null>(
        `/api/truemains/${encodeURIComponent(nameTagRef.value)}/rank-history`,
        {
          query: { days: daysRef.value },
          ignoreResponseError: true,
        },
      )

      if (!response || typeof response !== 'object' || !Array.isArray(response.entries)) {
        notFound.value = true
        data.value = null
        return
      }

      notFound.value = false
      data.value = response
    }
    catch (err) {
      error.value = err
    }
    finally {
      isLoading.value = false
      isInitialLoading.value = false
    }
  }

  watch([nameTagRef, daysRef], () => { void fetchHistory() }, { immediate: true })

  return {
    data,
    isLoading,
    isInitialLoading,
    notFound,
    error,
  }
}
