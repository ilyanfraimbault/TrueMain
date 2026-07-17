import type { CompositionBuildRequest, CompositionBuildResponse } from '~~/shared/types/composition'

/**
 * Imperative client for `POST /champions/{id}/composition-build`. Hand-rolled
 * refs instead of `useAsyncData` — the recommendation is a command fired on
 * submit, not a cache-keyed read (the 30s response cache lives server-side,
 * keyed on the normalised draft).
 */
export function useCompositionBuild() {
  const data = ref<CompositionBuildResponse | null>(null)
  const isLoading = ref(false)
  const error = ref<unknown>(null)

  async function submit(championId: number, body: CompositionBuildRequest) {
    isLoading.value = true
    error.value = null
    try {
      data.value = await $fetch<CompositionBuildResponse>(
        `/api/champions/${championId}/composition-build`,
        { method: 'POST', body },
      )
    }
    catch (err) {
      error.value = err
      data.value = null
    }
    finally {
      isLoading.value = false
    }
  }

  function clear() {
    data.value = null
    error.value = null
  }

  return { data, isLoading, error, submit, clear }
}
