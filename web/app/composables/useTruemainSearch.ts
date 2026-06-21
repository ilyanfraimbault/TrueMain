import type { SearchResponse, SearchResult } from '~~/shared/types/search'

/** Below this many characters the backend won't search, so neither do we. */
export const SEARCH_MIN_LENGTH = 2

type SearchStatus = 'idle' | 'pending' | 'success' | 'error'

/**
 * Debounced truemain name/tag lookup. Watches a reactive search term, waits
 * for typing to settle, then hits `GET /api/truemains/search`. Stale responses
 * (a slow request that resolves after a newer keystroke) are dropped via a
 * monotonic request token so the list never flickers back to an old result.
 *
 * Purely client-side — there's no SSR value to hydrate for a search box, and
 * the term only exists once the user starts typing.
 */
export function useTruemainSearch(term: MaybeRefOrGetter<string>) {
  const results = ref<SearchResult[]>([])
  const status = ref<SearchStatus>('idle')

  const normalized = computed(() => toValue(term).trim())
  const tooShort = computed(() => normalized.value.length < SEARCH_MIN_LENGTH)

  let debounceTimer: ReturnType<typeof setTimeout> | null = null
  let latestToken = 0

  async function run(query: string, token: number) {
    status.value = 'pending'
    try {
      const data = await $fetch<SearchResponse>('/api/truemains/search', { query: { q: query } })
      // Drop the response if a newer keystroke has since fired — only the
      // most recent request is allowed to write the results.
      if (token !== latestToken) return
      results.value = data.results
      status.value = 'success'
    }
    catch {
      if (token !== latestToken) return
      results.value = []
      status.value = 'error'
    }
  }

  watch(normalized, (value) => {
    if (debounceTimer) clearTimeout(debounceTimer)

    // Invalidate any in-flight request — its response must not land.
    latestToken += 1
    const token = latestToken

    if (value.length < SEARCH_MIN_LENGTH) {
      results.value = []
      status.value = 'idle'
      return
    }

    debounceTimer = setTimeout(() => { void run(value, token) }, 250)
  })

  onScopeDispose(() => {
    if (debounceTimer) clearTimeout(debounceTimer)
  })

  return { results, status, normalized, tooShort }
}
