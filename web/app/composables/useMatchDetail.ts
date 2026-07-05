import type { MatchDetailResponse } from '~~/shared/types/match-detail'

/**
 * Single-match detail fetch for `GET /truemains/{nameTag}/matches/{matchId}`,
 * proxied through `/api/**`. Client-only (`server: false`) — the payload is
 * large and viewer-agnostic, so there's nothing to gain from SSR. A 404 from
 * the controller (malformed name tag, unknown account, or a match the account
 * never played) is surfaced as `notFound = true` so the page renders an empty
 * state instead of an error.
 */
export function useMatchDetail(
  nameTag: MaybeRefOrGetter<string>,
  matchId: MaybeRefOrGetter<string>,
) {
  const nameTagRef = computed(() => toValue(nameTag))
  const matchIdRef = computed(() => toValue(matchId))

  const notFound = ref(false)

  const key = computed(() => `match-detail-${nameTagRef.value}-${matchIdRef.value}`)

  const { data, pending, error, refresh } = useLazyAsyncData<MatchDetailResponse | null>(
    key,
    async () => {
      notFound.value = false

      if (!nameTagRef.value || !matchIdRef.value) {
        notFound.value = true
        return null
      }

      const response = await $fetch<MatchDetailResponse | null>(
        `/api/truemains/${encodeURIComponent(nameTagRef.value)}/matches/${encodeURIComponent(matchIdRef.value)}`,
        { ignoreResponseError: true },
      )

      // `ignoreResponseError: true` turns the controller's 404 into a null
      // body — the only way to tell "not found" apart from a real payload is
      // the shape check. Anything missing the participants array is a 404.
      if (!response || !Array.isArray(response.participants)) {
        notFound.value = true
        return null
      }

      return response
    },
    {
      server: false,
      watch: [nameTagRef, matchIdRef],
    },
  )

  return {
    data,
    isLoading: pending,
    notFound,
    error,
    refresh,
  }
}
