import type { LeaderboardResponse, LeaderboardRowResponse, RegionSlug } from '~~/shared/types/leaderboard'

interface UseTruemainsLeaderboardOptions {
  /** Page size to request per fetch. Omitted = use the backend default (25). */
  pageSize?: number
  region?: MaybeRefOrGetter<RegionSlug | null | undefined>
  position?: MaybeRefOrGetter<string | null | undefined>
  championId?: MaybeRefOrGetter<number | null | undefined>
}

/**
 * Page-paginated truemains leaderboard. Exposes the current page of rows
 * plus the total count so the caller can drive a <c>UPagination</c> control.
 * Refetches whenever the page or any filter ref changes.
 *
 * No `notFound` flag — the endpoint is global and always returns an envelope
 * (empty rows array when the filter matches no accounts).
 */
export function useTruemainsLeaderboard(
  page: MaybeRefOrGetter<number>,
  options: UseTruemainsLeaderboardOptions = {},
) {
  const pageRef = computed(() => {
    const value = toValue(page)
    return Number.isFinite(value) && value >= 1 ? Math.floor(value) : 1
  })
  const regionRef = computed(() => {
    const value = toValue(options.region)
    return value ? value : null
  })
  const positionRef = computed(() => {
    const value = toValue(options.position)
    return value ? value : null
  })
  const championIdRef = computed(() => {
    const value = toValue(options.championId)
    return typeof value === 'number' && value > 0 ? value : null
  })

  const rows = ref<LeaderboardRowResponse[]>([])
  const total = ref(0)
  const pageSize = ref(options.pageSize ?? 25)
  const isLoading = ref(false)
  const isInitialLoading = ref(true)
  const error = ref<unknown>(null)

  async function fetchPage() {
    isLoading.value = true
    error.value = null
    try {
      const query: Record<string, string | number> = {
        page: pageRef.value,
      }
      if (options.pageSize != null) query.pageSize = options.pageSize
      if (regionRef.value) query.region = regionRef.value
      if (positionRef.value) query.position = positionRef.value
      if (championIdRef.value) query.championId = championIdRef.value

      const response = await $fetch<LeaderboardResponse>('/api/truemains', { query })

      rows.value = response.rows ?? []
      total.value = response.total ?? 0
      pageSize.value = response.pageSize ?? pageSize.value
    }
    catch (err) {
      error.value = err
      rows.value = []
      total.value = 0
    }
    finally {
      isLoading.value = false
      isInitialLoading.value = false
    }
  }

  watch(
    [pageRef, regionRef, positionRef, championIdRef],
    () => { void fetchPage() },
    { immediate: true },
  )

  return {
    rows,
    total,
    pageSize,
    isLoading,
    isInitialLoading,
    error,
    refresh: fetchPage,
  }
}
