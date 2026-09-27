import type { CompositionBuildRequest, CompositionBuildResponse } from '~~/shared/types/composition'

/**
 * Imperative client for `POST /champions/{id}/composition-build`. Hand-rolled
 * refs instead of `useAsyncData` — the recommendation is a command re-fired on
 * every draft edit, not a cache-keyed read (the 30s response cache lives
 * server-side, keyed on the normalised draft).
 *
 * Tuned for the live-updating builder: `data` survives the next request rather
 * than being cleared on submit, so a consumer can choose to keep rendering it
 * (the matchup page no longer does — it shows skeletons while `isLoading`), and
 * a request counter drops out-of-order responses so a slow older query can never
 * overwrite a newer draft's result.
 */
export function useCompositionBuild() {
  const data = ref<CompositionBuildResponse | null>(null)
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

  async function submit(championId: number, body: CompositionBuildRequest) {
    const seq = ++requestSeq
    abortInFlight()
    controller = new AbortController()
    isLoading.value = true
    error.value = null
    try {
      const response = await $fetch<CompositionBuildResponse>(
        `/api/champions/${championId}/composition-build`,
        { method: 'POST', body, signal: controller.signal },
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

  return { data, isLoading, error, submit, clear }
}
