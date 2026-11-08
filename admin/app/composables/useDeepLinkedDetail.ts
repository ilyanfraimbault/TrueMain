import { useKeyedFetch } from '~/composables/useKeyedFetch'
import { keyedId } from '~/utils/keyed-fetch'

interface DeepLinkedDetailOptions<T> {
  /** Route query key that mirrors the open detail id (e.g. `candidate`). */
  queryKey: string
  /** Fetch the detail payload for one id. */
  fetch: (id: string) => Promise<T>
  /** Message shown when the backend answers 404 for the id. */
  notFoundMessage: (id: string) => string
  /** Fallback message when the fetch fails without a usable error body. */
  loadErrorMessage: string
  /** Invoked with the id when a deep link opens the detail on initial load. */
  onDeepLink?: (id: string) => void
}

// Deep-linkable detail slide-over state, shared by the candidates and
// data-quality pages. An imperative `openDetail(id)` drives the slide-over and
// reflects the open id in the URL (`?<queryKey>=ID`) so the view is shareable;
// closing drops the query param so a refresh doesn't re-open it unexpectedly,
// and a deep link on initial load opens the detail straight away.
export function useDeepLinkedDetail<T>(options: DeepLinkedDetailOptions<T>) {
  const route = useRoute()
  const router = useRouter()

  const detailOpen = ref(false)
  // One keyed fetch (#2471): a slow answer for the previous id can neither land
  // under the one open now nor clear its loading state.
  const { state, load } = useKeyedFetch<string, T>(id => options.fetch(id))
  const detailId = computed(() => keyedId(state.value))
  const detail = computed(() => state.value.s === 'ready' ? state.value.value : null)
  const detailPending = computed(() => state.value.s === 'loading')
  const failure = computed(() => state.value.s === 'error' ? state.value : null)
  // A 404 is an expected outcome, not something worth correlating to a server log
  // line, so only a genuine backend/network failure carries a trace id.
  const detailError = computed(() => {
    const failed = failure.value
    if (!failed) {
      return null
    }
    return isNotFound(failed.error) ? options.notFoundMessage(failed.id) : extractFetchError(failed.error, options.loadErrorMessage)
  })
  const detailErrorTraceId = computed(() => {
    const failed = failure.value
    return failed && !isNotFound(failed.error) ? extractFetchErrorTraceId(failed.error) : undefined
  })

  async function openDetail(rawId: string) {
    const id = rawId.trim()
    if (!id) {
      return
    }
    detailOpen.value = true
    // Reflect the open detail in the URL so the view is deep-linkable.
    if (route.query[options.queryKey] !== id) {
      router.replace({ query: { ...route.query, [options.queryKey]: id } })
    }
    await load(id)
  }

  // Drop the query param when the slide-over closes so a refresh doesn't
  // re-open it unexpectedly.
  watch(detailOpen, (open) => {
    if (!open && route.query[options.queryKey]) {
      const { [options.queryKey]: _removed, ...rest } = route.query
      router.replace({ query: rest })
    }
  })

  // Deep-link: open the slide-over on initial load when the query key is present.
  // `openDetail` reports its own failures in `detailError`, so awaiting it is enough.
  onMounted(async () => {
    const id = queryString(route.query[options.queryKey])
    if (id) {
      options.onDeepLink?.(id)
      await openDetail(id)
    }
  })
  // …and when it changes on a page already open — a ⌘K jump (#2290). `openDetail`
  // mirrors its own id into the URL, which lands here as the id already shown.
  useQueryValue(options.queryKey, async (id) => {
    if (id !== detailId.value || !detailOpen.value) {
      options.onDeepLink?.(id)
      await openDetail(id)
    }
  })

  return { state, detailOpen, detail, detailPending, detailError, detailErrorTraceId, detailId, openDetail }
}

function isNotFound(error: unknown): boolean {
  return (error as { statusCode?: number } | null)?.statusCode === 404
}
