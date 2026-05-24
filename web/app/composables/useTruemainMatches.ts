import type { MatchSummariesResponse, MatchSummaryResponse } from '~~/shared/types/matches'

interface UseTruemainMatchesOptions {
  /** Page size to request per fetch. Defaults to the backend default (20). */
  pageSize?: number
  /**
   * Riot team position to filter on (`TOP` / `JUNGLE` / `MIDDLE` /
   * `BOTTOM` / `UTILITY`). Pass an empty string or null to clear.
   */
  position?: MaybeRefOrGetter<string | null | undefined>
  /** Champion id to filter on. Pass null/undefined or 0 to clear. */
  championId?: MaybeRefOrGetter<number | null | undefined>
}

/**
 * Page-paginated match history for a given <c>nameTag</c>. Exposes the
 * current page of matches plus the total count so the caller can drive a
 * <c>UPagination</c> control. Refetches whenever the name tag, page, or
 * any filter change.
 *
 * `null` from the API (malformed nameTag or no matching Riot account) is
 * surfaced as <c>notFound = true</c> — the page renders an empty state
 * rather than an error.
 */
export function useTruemainMatches(
  nameTag: MaybeRefOrGetter<string>,
  page: MaybeRefOrGetter<number>,
  options: UseTruemainMatchesOptions = {},
) {
  const nameTagRef = computed(() => toValue(nameTag))
  const pageRef = computed(() => {
    const value = toValue(page)
    return Number.isFinite(value) && value >= 1 ? Math.floor(value) : 1
  })
  const positionRef = computed(() => {
    const value = toValue(options.position)
    return value ? value : null
  })
  const championIdRef = computed(() => {
    const value = toValue(options.championId)
    return typeof value === 'number' && value > 0 ? value : null
  })

  const matches = ref<MatchSummaryResponse[]>([])
  const total = ref(0)
  const pageSize = ref(options.pageSize ?? 20)
  const isLoading = ref(false)
  const isInitialLoading = ref(true)
  const notFound = ref(false)
  const error = ref<unknown>(null)

  async function fetchPage() {
    if (!nameTagRef.value) {
      matches.value = []
      total.value = 0
      notFound.value = false
      isInitialLoading.value = false
      return
    }

    isLoading.value = true
    error.value = null
    try {
      const query: Record<string, string | number> = {
        page: pageRef.value,
      }
      if (options.pageSize != null) query.pageSize = options.pageSize
      if (positionRef.value) query.position = positionRef.value
      if (championIdRef.value) query.championId = championIdRef.value

      const response = await $fetch<MatchSummariesResponse | null>(
        `/api/truemains/${encodeURIComponent(nameTagRef.value)}/matches`,
        {
          query,
          ignoreResponseError: true,
        },
      )

      // 404 from the controller bubbles up as null thanks to
      // `ignoreResponseError`. Anything else missing the matches array we
      // also treat as not-found — same contract as the profile composable.
      if (!response || !Array.isArray(response.matches)) {
        notFound.value = true
        matches.value = []
        total.value = 0
        return
      }

      notFound.value = false
      matches.value = response.matches
      total.value = response.total
      pageSize.value = response.pageSize
    }
    catch (err) {
      error.value = err
    }
    finally {
      isLoading.value = false
      isInitialLoading.value = false
    }
  }

  // Refetch on any reactive arg change. Empty nameTag short-circuits inside
  // fetchPage so a parent page can bind to a still-empty ref without
  // triggering a 404 round trip on the first tick.
  watch(
    [nameTagRef, pageRef, positionRef, championIdRef],
    () => { void fetchPage() },
    { immediate: true },
  )

  return {
    matches,
    total,
    pageSize,
    isLoading,
    isInitialLoading,
    notFound,
    error,
    refresh: fetchPage,
  }
}
