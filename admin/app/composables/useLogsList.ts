import type { LogLevel } from '~~/shared/types/logs'

/**
 * The Logs tab of `/logs`: its filters, the server-paginated `GET /api/ops/logs`
 * fetch they drive, and the checkbox selection (#722). Split out of
 * `pages/logs.vue` (#1436) in the order the page declared them, so the filter
 * watchers still run after the fetch's own.
 *
 * Filterable by minimum severity (`level`), category, named ops event
 * (`eventType`, exact match against the backend's static catalog), producing
 * process, exception presence, relative time window and a free-text search
 * (matched against message + exception). The endpoint paginates, so only one page
 * is ever held in memory.
 */
export function useLogsList() {
  const route = useRoute()
  const router = useRouter()

  // `level` is a MINIMUM threshold: Warning returns Warning + Error + Critical.
  // It defaults to Warning (#1415) so the first paint is failures, not a wall of
  // `Information`; an explicit `?level=` (including `?level=all`) still wins.
  const level = ref<'all' | LogLevel>(parseLevelQuery(route.query.level))
  // Named ops event (exact match); the option list comes from the response's
  // static `eventTypes` catalog.
  const eventType = ref<string>(ALL)
  // Producing process ("Api"/"Ingestor"); catalog rides on the response.
  const process = ref<string>(ALL)
  // True keeps only rows carrying a formatted exception.
  const exceptionsOnly = ref(false)
  const category = ref('')
  // Relative window -> ISO `since`. "All" omits the param.
  const sinceWindow = ref<SinceWindow>(ALL)
  // Raw search input; debounced before it hits the query so typing doesn't fire a
  // request per keystroke.
  const searchInput = ref('')
  const search = refDebounced(searchInput, 300)

  const page = ref(1)
  const pageSize = 50

  const levelItems = [
    { label: 'All levels', value: ALL },
    ...LOG_LEVELS.map(name => ({ label: name, value: name })),
  ]

  // Selecting a level rewrites `?level=` so the current view is always a shareable
  // link — and so a reload doesn't silently snap back to the default.
  watch(level, (value) => {
    router.replace({
      query: {
        ...route.query,
        level: value === DEFAULT_LOG_LEVEL ? undefined : value,
      },
    })
  })

  function showAllLevels() {
    level.value = ALL
  }

  const filters = computed(() => ({
    level: level.value === ALL ? undefined : level.value,
    category: category.value.trim() || undefined,
    since: sinceWindow.value === ALL
      ? undefined
      : sinceToIso(sinceWindow.value),
    search: search.value.trim() || undefined,
    eventType: eventType.value === ALL ? undefined : eventType.value,
    process: process.value === ALL ? undefined : process.value,
    hasException: exceptionsOnly.value || undefined,
    page: page.value,
    pageSize,
  }))

  const hasActiveFilters = computed(() =>
    Boolean(
      level.value !== DEFAULT_LOG_LEVEL
      || eventType.value !== ALL
      || process.value !== ALL
      || exceptionsOnly.value
      || category.value.trim()
      || sinceWindow.value !== ALL
      || searchInput.value.trim(),
    ),
  )
  // "Clear" returns to the page's default view (Warning and above), not to a
  // filterless one — the errors-first default is the resting state.
  function resetFilters() {
    level.value = DEFAULT_LOG_LEVEL
    eventType.value = ALL
    process.value = ALL
    exceptionsOnly.value = false
    category.value = ''
    sinceWindow.value = ALL
    searchInput.value = ''
  }

  const { data, pending, error, refresh } = useLogs(filters)

  const entries = computed(() => data.value?.entries ?? [])
  const total = computed(() => data.value?.total ?? 0)
  // The page the server actually served (its clamp wins over our optimistic ref).
  const serverPage = computed(() => data.value?.page ?? page.value)
  const serverPageSize = computed(() => data.value?.pageSize ?? pageSize)

  // Event filter options: the response carries the backend's static catalog of
  // known event names on every page, so no extra request (or Mongo distinct) is
  // needed. Empty until the first response lands.
  const eventItems = computed(() => [
    { label: 'All events', value: ALL },
    ...(data.value?.eventTypes ?? []).map(name => ({ label: name, value: name })),
  ])

  // Process filter options — same static-catalog-on-response pattern as events.
  const processItems = computed(() => [
    { label: 'All processes', value: ALL },
    ...(data.value?.processes ?? []).map(name => ({ label: name, value: name })),
  ])

  // Any filter change must reset to the first page — otherwise a narrower filter
  // could leave us stranded on a now-out-of-range page. `search` (debounced) is
  // watched rather than the raw input so the reset lands with the actual query.
  watch([level, eventType, process, exceptionsOnly, category, sinceWindow, search], () => {
    page.value = 1
  })

  // Checkbox multi-select keyed by entry id (`get-row-id`), so the selection maps
  // straight back to entries. Cleared whenever the visible set changes (filter or
  // page) — a hidden selection would silently ride into the copied JSON.
  const rowSelection = ref<Record<string, boolean>>({})
  watch([filters], () => {
    rowSelection.value = {}
  })

  const selectedEntries = computed(() =>
    entries.value.filter(entry => rowSelection.value[String(entry.id)]))
  // Full entries, pretty-printed — not the truncated table cells.
  const selectionJson = computed(() => JSON.stringify(selectedEntries.value, null, 2))

  return {
    level,
    eventType,
    process,
    exceptionsOnly,
    category,
    sinceWindow,
    searchInput,
    page,
    levelItems,
    eventItems,
    processItems,
    showAllLevels,
    hasActiveFilters,
    resetFilters,
    pending,
    error,
    refresh,
    entries,
    total,
    serverPage,
    serverPageSize,
    rowSelection,
    selectedEntries,
    selectionJson,
  }
}
