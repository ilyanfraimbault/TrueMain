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
 * The initial render is server-side: this uses `useAsyncData` with
 * `server: true`, so the first page of rows is baked into the SSR'd HTML and
 * reused verbatim on hydration (Nuxt serializes the resolved payload, so the
 * server markup and the client's first render are identical — no `<!-- -->`
 * vs `<div>` mismatch). Subsequent filter / page changes only fire the watcher
 * after hydration, so those refetches run client-side as required.
 *
 * Unlike pages/champions/index.vue (which deliberately stays `server: false`),
 * the leaderboard is a single keyed source with server-side pagination, so
 * there is no second async source whose resolve timing could diverge between
 * the server and the client and bake a different `v-if` branch into each.
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

  function buildQuery() {
    const query: Record<string, string | number> = {
      page: pageRef.value,
    }
    if (options.pageSize != null) query.pageSize = options.pageSize
    if (regionRef.value) query.region = regionRef.value
    if (positionRef.value) query.position = positionRef.value
    if (championIdRef.value) query.championId = championIdRef.value
    return query
  }

  const fallbackPageSize = options.pageSize ?? 25

  // `useAsyncData` (not `useFetch`) so the cache key is the page + filter
  // signature rather than the request URL — keeps the key stable and explicit,
  // and the watcher list below drives the refetch. The key is shared across
  // the SSR payload and the client so hydration reuses the server's rows.
  const { data, status, error, refresh } = useAsyncData<LeaderboardResponse>(
    () => {
      const region = regionRef.value ?? 'all'
      const position = positionRef.value ?? 'all'
      const championId = championIdRef.value ?? 'all'
      return `truemains-leaderboard-${pageRef.value}-${region}-${position}-${championId}`
    },
    () => $fetch<LeaderboardResponse>('/api/truemains', { query: buildQuery() }),
    {
      server: true,
      watch: [pageRef, regionRef, positionRef, championIdRef],
      // Deterministic placeholder so `rows` is always an array (never
      // `undefined`) on both the server and the client's first render.
      default: (): LeaderboardResponse => ({
        rows: [],
        page: pageRef.value,
        pageSize: fallbackPageSize,
        total: 0,
      }),
    },
  )

  const rows = computed<LeaderboardRowResponse[]>(() => data.value?.rows ?? [])
  const total = computed(() => data.value?.total ?? 0)
  const pageSize = computed(() => data.value?.pageSize ?? fallbackPageSize)

  const isLoading = computed(() => status.value === 'pending')

  // One-way latch: the skeleton shows only until the very first response
  // settles, then never again. Deriving this purely from `status` (e.g.
  // `pending && rows.length === 0`) wrongly re-shows the full skeleton on a
  // *later* refetch that starts from an empty result set — paging away from a
  // zero-match filter, say — because that refetch is `pending` with no current
  // rows. Latching reproduces the old one-shot flag.
  //
  // SSR-safe: on the server `server: true` resolves the payload (via Suspense)
  // before render, so the skeleton must never appear there — seed the latch
  // `true` with `import.meta.server`. Seeding from `status` instead would read
  // `pending` at synchronous setup (the fetch settles later, and watchers don't
  // run during SSR), baking the skeleton into the server HTML while the client
  // hydrates `status` to `success` synchronously and renders rows — a hydration
  // mismatch. On the client the same synchronous `success`/`error` seeds the
  // ref `true`, so the branch matches the server. Only a fresh client-side
  // navigation seeds `false` and shows the skeleton once, until the first
  // response settles.
  const hasEverLoaded = ref(
    import.meta.server || status.value === 'success' || status.value === 'error',
  )
  watch(status, (s) => {
    if (s === 'success' || s === 'error') hasEverLoaded.value = true
  })
  const isInitialLoading = computed(() => !hasEverLoaded.value)

  return {
    rows,
    total,
    pageSize,
    isLoading,
    isInitialLoading,
    error,
    refresh,
  }
}
