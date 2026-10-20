import type { LiveGameResponse } from '#shared/types/live'

interface UseTruemainLiveOptions {
  /**
   * Hold the request back while false: the Live tab asks Riot only once it is opened,
   * never on a profile view that stays on the history (#2103).
   */
  enabled?: MaybeRefOrGetter<boolean>
}

/**
 * The game a player is in right now (#2103), read from spectator-v5 through
 * `GET /truemains/{nameTag}/live`. On demand only: one request when the tab opens, and
 * one per "Check now" (`check()`), which asks the API to bypass its 30 s cache — the
 * API still refuses to ask Riot twice within 10 s. Nothing polls in the background.
 *
 * Client-only like the other profile fetches — see {@link useTruemainFetch}.
 */
export function useTruemainLive(
  nameTag: MaybeRefOrGetter<string>,
  options: UseTruemainLiveOptions = {},
) {
  const data = ref<LiveGameResponse | null>(null)
  const apiFetch = useApiFetch()
  // Set by `check()` for the one request it issues, so a tab re-open or a name-tag
  // change still reads the cached answer.
  let bypassCache = false

  const { isLoading, isInitialLoading, notFound, error, execute, ready } = useTruemainFetch<LiveGameResponse>(nameTag, {
    enabled: options.enabled,
    request: (tag, signal) => {
      const refresh = bypassCache
      bypassCache = false
      return apiFetch<LiveGameResponse | null>(
        `/truemains/${encodeURIComponent(tag)}/live`,
        { ignoreResponseError: true, signal, query: refresh ? { refresh: true } : undefined },
      )
    },
    validate: (response): response is LiveGameResponse =>
      Boolean(response && typeof response === 'object' && typeof response.status === 'string'),
    onResponse: (response) => { data.value = response },
    onClear: () => { data.value = null },
  })

  async function check() {
    bypassCache = true
    await execute()
  }

  return {
    data,
    isLoading,
    isInitialLoading,
    notFound,
    error,
    ready,
    check,
  }
}
