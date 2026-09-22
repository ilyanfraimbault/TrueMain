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

  async function submit(championId: number, body: CompositionBuildRequest) {
    const seq = ++requestSeq
    isLoading.value = true
    error.value = null
    try {
      const response = await $fetch<CompositionBuildResponse>(
        `/api/champions/${championId}/composition-build`,
        { method: 'POST', body },
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
    data.value = null
    error.value = null
    isLoading.value = false
  }

  return { data, isLoading, error, submit, clear }
}
