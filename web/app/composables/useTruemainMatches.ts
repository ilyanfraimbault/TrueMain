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
 * rather than an error. Same contract as the profile composable, shared via
 * {@link useTruemainFetch}.
 */
export function useTruemainMatches(
  nameTag: MaybeRefOrGetter<string>,
  page: MaybeRefOrGetter<number>,
  options: UseTruemainMatchesOptions = {},
) {
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

  const { isLoading, isInitialLoading, notFound, error, execute } = useTruemainFetch<MatchSummariesResponse>(nameTag, {
    watch: [pageRef, positionRef, championIdRef],
    request: (tag) => {
      const query: Record<string, string | number> = {
        page: pageRef.value,
      }
      if (options.pageSize != null) query.pageSize = options.pageSize
      if (positionRef.value) query.position = positionRef.value
      if (championIdRef.value) query.championId = championIdRef.value

      return $fetch<MatchSummariesResponse | null>(
        `/api/truemains/${encodeURIComponent(tag)}/matches`,
        {
          query,
          ignoreResponseError: true,
        },
      )
    },
    // Anything missing the matches array we treat as not-found.
    validate: (response): response is MatchSummariesResponse =>
      Boolean(response && Array.isArray(response.matches)),
    onResponse: (response) => {
      matches.value = response.matches
      total.value = response.total
      pageSize.value = response.pageSize
    },
    onClear: () => {
      matches.value = []
      total.value = 0
    },
  })

  return {
    matches,
    total,
    pageSize,
    isLoading,
    isInitialLoading,
    notFound,
    error,
    refresh: execute,
  }
}
