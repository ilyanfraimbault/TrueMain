import type { CompositionBuildGamesResponse, CompositionBuildRequest } from '#shared/types/composition'

/**
 * Imperative client for `POST /champions/{id}/composition-build/games`
 * (#940) — the provenance listing behind the confidence strip's "games used"
 * stat. Same shape as {@link useCompositionBuild}: hand-rolled refs, a request
 * counter dropping out-of-order responses, so a stale page load from a
 * since-changed draft can never overwrite a fresher one.
 *
 * Kept separate from the recommendation composable on purpose — the drawer
 * fetches only when opened, on its own page, and must never fire on every
 * draft-edit debounce the way the recommendation does.
 */
export function useCompositionBuildGames() {
  const apiFetch = useApiFetch()
  const data = ref<CompositionBuildGamesResponse | null>(null)
  const isLoading = ref(false)
  const error = ref<unknown>(null)
  let requestSeq = 0
  // The token only stops a superseded response from being written; aborting
  // stops it from being fetched at all, so a burst of draft edits no longer
  // leaves every older request running to completion (#1712).
  let controller: AbortController | null = null

  function abortInFlight() {
    controller?.abort()
    controller = null
  }

  async function fetchPage(championId: number, body: CompositionBuildRequest, page: number, pageSize?: number) {
    const seq = ++requestSeq
    abortInFlight()
    controller = new AbortController()
    isLoading.value = true
    error.value = null
    try {
      const response = await apiFetch<CompositionBuildGamesResponse>(
        `/champions/${championId}/composition-build/games`,
        {
          method: 'POST',
          body,
          query: { page, ...(pageSize ? { pageSize } : {}) },
          signal: controller.signal,
        },
      )
      if (seq === requestSeq) {
        data.value = response
      }
    }
    catch (err) {
      if (seq === requestSeq) {
        error.value = err
        data.value = null
      }
    }
    finally {
      if (seq === requestSeq) {
        isLoading.value = false
      }
    }
  }

  function clear() {
    requestSeq++
    abortInFlight()
    data.value = null
    error.value = null
    isLoading.value = false
  }

  // Leaving the page cancels whatever is still in flight. Bumping the token
  // first keeps the aborted request's rejection out of `error`.
  onScopeDispose(() => {
    requestSeq++
    abortInFlight()
  })

  return { data, isLoading, error, fetchPage, clear }
}
