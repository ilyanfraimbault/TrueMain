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
    detail.value = null
    // Reflect the open detail in the URL so the view is deep-linkable.
    if (route.query[options.queryKey] !== id) {
      router.replace({ query: { ...route.query, [options.queryKey]: id } })
    }
    try {
      detail.value = await options.fetch(id)
    }
    catch (err: unknown) {
      detailError.value = (err as { statusCode?: number })?.statusCode === 404
        ? options.notFoundMessage(id)
        : extractFetchError(err, options.loadErrorMessage)
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
  onMounted(() => {
    const fromQuery = route.query[options.queryKey]
    const id = Array.isArray(fromQuery) ? fromQuery[0] : fromQuery
    if (id) {
      options.onDeepLink?.(id)
      openDetail(id)
    }
  })

  return { detailOpen, detail, detailPending, detailError, detailId, openDetail }
}
