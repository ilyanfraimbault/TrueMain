import type { Ref } from 'vue'

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
  const detail = ref(null) as Ref<T | null>
  const detailPending = ref(false)
  const detailError = ref<string | null>(null)
  // Only set for a genuine backend/network failure, never for the 404 case —
  // a missing id is an expected outcome, not something worth correlating to a
  // server log line.
  const detailErrorTraceId = ref<string | undefined>(undefined)
  const detailId = ref<string | null>(null)

  async function openDetail(rawId: string) {
    const id = rawId.trim()
    if (!id) {
      return
    }
    detailId.value = id
    detailOpen.value = true
    detailPending.value = true
    detailError.value = null
    detailErrorTraceId.value = undefined
    detail.value = null
    // Reflect the open detail in the URL so the view is deep-linkable.
    if (route.query[options.queryKey] !== id) {
      router.replace({ query: { ...route.query, [options.queryKey]: id } })
    }
    try {
      detail.value = await options.fetch(id)
    }
    catch (err: unknown) {
      if ((err as { statusCode?: number })?.statusCode === 404) {
        detailError.value = options.notFoundMessage(id)
      }
      else {
        detailError.value = extractFetchError(err, options.loadErrorMessage)
        detailErrorTraceId.value = extractFetchErrorTraceId(err)
      }
    }
    finally {
      detailPending.value = false
    }
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

  return { detailOpen, detail, detailPending, detailError, detailErrorTraceId, detailId, openDetail }
}
