import type { CompositionBuildGamesResponse, CompositionBuildRequest } from '~~/shared/types/composition'

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
  const data = ref<CompositionBuildGamesResponse | null>(null)
  const isLoading = ref(false)
  const error = ref<unknown>(null)
  let requestSeq = 0

  async function fetchPage(championId: number, body: CompositionBuildRequest, page: number, pageSize?: number) {
    const seq = ++requestSeq
    isLoading.value = true
    error.value = null
    try {
      const response = await $fetch<CompositionBuildGamesResponse>(
        `/api/champions/${championId}/composition-build/games`,
        {
          method: 'POST',
          body,
          query: { page, ...(pageSize ? { pageSize } : {}) },
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
    data.value = null
    error.value = null
    isLoading.value = false
  }

  return { data, isLoading, error, fetchPage, clear }
}
