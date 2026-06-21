import type { SearchResponse, SearchResult } from '~~/shared/types/search'

/** Below this many characters the backend won't search, so neither do we. */
export const SEARCH_MIN_LENGTH = 2

/** Idle time after the last keystroke before a search fires. */
export const SEARCH_DEBOUNCE_MS = 250

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

  // The backend only searches the game-name part — the bit before '#' — so the
  // "too short" guard must measure that, not the whole Name#TAG string.
  // Otherwise "a#NA1" (5 chars) slips past, fires a request the backend answers
  // empty, and the user sees "no match" instead of the "keep typing" hint.
  const namePart = computed(() => {
    const value = normalized.value
    const hash = value.indexOf('#')
    return (hash >= 0 ? value.slice(0, hash) : value).trim()
  })
  const tooShort = computed(() => namePart.value.length < SEARCH_MIN_LENGTH)

  let debounceTimer: ReturnType<typeof setTimeout> | null = null
  let latestToken = 0
  let controller: AbortController | null = null

  async function run(query: string, token: number) {
    controller = new AbortController()
    const { signal } = controller
    status.value = 'pending'
    try {
      const data = await $fetch<SearchResponse>('/api/truemains/search', {
        query: { q: query },
        signal,
      })
      // Drop the response if a newer keystroke has since fired — only the
      // most recent request is allowed to write the results.
      if (token !== latestToken) return
      results.value = data.results
      status.value = 'success'
    }
    catch (error) {
      // A request superseded by a newer keystroke is already filtered by the
      // token guard. The remaining abort case is scope disposal (the modal
      // closed mid-flight), which keeps the current token — `signal.aborted`
      // catches it regardless of how $fetch wraps the abort, so swallow it.
      // Anything else is a real failure: surface it to the user and log it so
      // prod network/parse errors aren't lost in a silent catch.
      if (token !== latestToken || signal.aborted) return
      console.error('[truemain-search] request failed', error)
      results.value = []
      status.value = 'error'
    }
  }

  watch(normalized, (value) => {
    if (debounceTimer) clearTimeout(debounceTimer)

    // Invalidate any in-flight request and abort it: the token guard already
    // ignores its response, but aborting also frees the wasted round trip so
    // fast typing on a slow link doesn't pile up concurrent requests.
    latestToken += 1
    controller?.abort()
    controller = null
    const token = latestToken

    if (namePart.value.length < SEARCH_MIN_LENGTH) {
      results.value = []
      status.value = 'idle'
      return
    }

    // Send the full term (tag included) — the gate is on the name part, but
    // the backend still uses the tag to narrow.
    debounceTimer = setTimeout(() => { void run(value, token) }, SEARCH_DEBOUNCE_MS)
  })

  onScopeDispose(() => {
    if (debounceTimer) clearTimeout(debounceTimer)
    controller?.abort()
  })

  return { results, status, tooShort }
}
