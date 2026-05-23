import type { MatchSummariesResponse } from '~~/shared/types/matches'

interface UseTruemainMatchesOptions {
  /** Page size to request per fetch. Defaults to the backend default (20). */
  pageSize?: number
}

/**
 * Cursor-paginated match history for a given <c>nameTag</c>. Exposes the
 * accumulated list across all pages plus a <c>loadMore</c> action so the
 * caller can drive an "infinite scroll" or a "Load more" button without
 * having to track the cursor.
 *
 * `null` from the API (malformed nameTag or no matching Riot account) is
 * surfaced as <c>notFound = true</c> — the page renders an empty state
 * rather than an error.
 */
export function useTruemainMatches(
  nameTag: MaybeRefOrGetter<string>,
  options: UseTruemainMatchesOptions = {},
) {
  const nameTagRef = computed(() => toValue(nameTag))

  const matches = ref<MatchSummariesResponse['matches']>([])
  const nextBefore = ref<string | null>(null)
  const isLoading = ref(false)
  const isInitialLoading = ref(true)
  const notFound = ref(false)
  const error = ref<unknown>(null)

  async function fetchPage(before: string | null) {
    isLoading.value = true
    error.value = null
    try {
      const response = await $fetch<MatchSummariesResponse | null>(
        `/api/truemains/${encodeURIComponent(nameTagRef.value)}/matches`,
        {
          query: {
            limit: options.pageSize,
            ...(before ? { before } : {}),
          },
          ignoreResponseError: true,
        },
      )

      // Server-mapped 404 surfaces as null because of `ignoreResponseError`.
      // Anything else with a missing body shape we also treat as not found —
      // matches the contract that an unknown nameTag returns 404.
      if (!response || !Array.isArray(response.matches)) {
        notFound.value = true
        matches.value = []
        nextBefore.value = null
        return
      }

      notFound.value = false
      matches.value = before === null
        ? response.matches
        : [...matches.value, ...response.matches]
      nextBefore.value = response.nextBefore ?? null
    }
    catch (err) {
      error.value = err
    }
    finally {
      isLoading.value = false
      isInitialLoading.value = false
    }
  }

  async function loadMore() {
    if (isLoading.value || nextBefore.value === null) return
    await fetchPage(nextBefore.value)
  }

  async function refresh() {
    matches.value = []
    nextBefore.value = null
    notFound.value = false
    isInitialLoading.value = true
    await fetchPage(null)
  }

  // Refetch when the nameTag changes (e.g. user navigates between profiles)
  // and on first mount, but skip the empty-string case so a parent page can
  // bind the input to a ref without triggering a 404 round-trip on every
  // keystroke before the user submits. Client-only — match history is
  // private-ish and we don't want to bake one user's PoV into SSR for another.
  watch(nameTagRef, (value) => {
    if (!value) {
      matches.value = []
      nextBefore.value = null
      notFound.value = false
      isInitialLoading.value = false
      return
    }
    void refresh()
  }, { immediate: true })

  return {
    matches: readonly(matches),
    nextBefore: readonly(nextBefore),
    isLoading: readonly(isLoading),
    isInitialLoading: readonly(isInitialLoading),
    notFound: readonly(notFound),
    error: readonly(error),
    hasMore: computed(() => nextBefore.value !== null),
    loadMore,
    refresh,
  }
}
