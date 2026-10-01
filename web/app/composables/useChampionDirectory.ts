import type { ChampionDirectoryResponse } from '~~/shared/types/champion-directory'
import type { ChampionSummaryResponse } from '~~/shared/types/champions'
import { DEFAULT_DIRECTORY_ORDER, type DirectoryOrder } from '~/utils/table-sorting'

interface UseChampionDirectoryOptions {
  patch: MaybeRefOrGetter<string | null | undefined>
  /** Cumulative "X+" threshold; omitted when falsy, as the API's default is every band. */
  eloBracket: MaybeRefOrGetter<string | null | undefined>
  truemainsOnly: MaybeRefOrGetter<boolean>
  position: MaybeRefOrGetter<string | null | undefined>
  championId: MaybeRefOrGetter<number | null | undefined>
  order: MaybeRefOrGetter<DirectoryOrder>
  page: MaybeRefOrGetter<number>
  pageSize: number
}

/**
 * One page of the champion directory (`GET /champions/directory`, #1734): the
 * API filters by lane and champion, orders and pages, so the listing never
 * downloads the whole directory. Refetches whenever a filter, the order or the
 * page changes.
 *
 * Client-only (`server: false`) like the page's static lookups — the #149
 * rationale on `pages/champions/index.vue`: the page renders its table in
 * `<ClientOnly>` with a skeleton fallback, so no fetch may race the SSR render.
 * Only non-default parameters are sent, so the resting request is the bare
 * `/champions/directory?pageSize=50` and shares one cache entry upstream.
 */
export function useChampionDirectory(options: UseChampionDirectoryOptions) {
  const query = computed(() => {
    const query: Record<string, string | number> = { pageSize: options.pageSize }
    const page = toValue(options.page)
    if (Number.isFinite(page) && page > 1) query.page = Math.floor(page)
    const patch = toValue(options.patch)
    if (patch) query.patch = patch
    const elo = toValue(options.eloBracket)
    if (elo) query.eloBracket = elo
    // Sent only when off — true is the API default.
    if (!toValue(options.truemainsOnly)) query.truemainsOnly = 'false'
    const position = toValue(options.position)
    if (position) query.position = position
    const championId = toValue(options.championId)
    if (typeof championId === 'number' && championId > 0) query.championId = championId
    const { sort, order } = toValue(options.order)
    if (sort !== DEFAULT_DIRECTORY_ORDER.sort) query.sort = sort
    if (order !== DEFAULT_DIRECTORY_ORDER.order) query.order = order
    return query
  })

  const apiFetch = useApiFetch()
  const directoryFetch = useAsyncData<ChampionDirectoryResponse>(
    () => `champion-directory-${Object.entries(query.value).map(([key, value]) => `${key}=${value}`).join('&')}`,
    (_nuxtApp, { signal }) => apiFetch<ChampionDirectoryResponse>('/champions/directory', { query: query.value, signal }),
    {
      server: false,
      watch: [query],
      default: (): ChampionDirectoryResponse => ({
        rows: [],
        page: 1,
        pageSize: options.pageSize,
        total: 0,
        patchVersion: '',
      }),
    },
  )
  const { data, status, error } = directoryFetch

  const rows = computed<ChampionSummaryResponse[]>(() => data.value?.rows ?? [])
  const total = computed(() => data.value?.total ?? 0)
  const pageSize = computed(() => data.value?.pageSize ?? options.pageSize)
  const patchVersion = computed(() => data.value?.patchVersion ?? '')
  const isLoading = computed(() => status.value === 'pending' || status.value === 'idle')

  // One-way latch, as in `useTruemainsLeaderboard`: the skeleton covers the
  // first load only; a later refetch keeps the rows on screen and shows the
  // table's loading bar instead. Client-only, so nothing settles on the server.
  const hasEverLoaded = ref(status.value === 'success' || status.value === 'error')
  watch(status, (value) => {
    if (value === 'success' || value === 'error') hasEverLoaded.value = true
  })
  const isInitialLoading = computed(() => !hasEverLoaded.value)

  return {
    rows,
    total,
    pageSize,
    patchVersion,
    isLoading,
    isInitialLoading,
    error,
    // A client-side navigation waits under the loading bar for the first page (#1689).
    ready: directoryFetch.then(() => undefined),
  }
}
