import type { MatchDetail } from '#shared/types/match-detail'
import { isLoadingStatus } from '#common/utils/async-data'
import { isNotFoundError } from '#common/utils/remote-read'

/**
 * Single-match detail fetch for `GET /truemains/{nameTag}/matches/{matchId}`,
 * proxied through `/api/**`. Client-only (`server: false`) — the payload is
 * large and viewer-agnostic, so there's nothing to gain from SSR. Backs the
 * inline detail panel of an expanded `MatchRow` accordion. A 404 from the
 * controller (malformed name tag, unknown account, or a match the account
 * never played) is surfaced as `notFound = true` so the panel renders an
 * empty state; any other failure is an `error` (#2476).
 */
export function useMatchDetail(
  nameTag: MaybeRefOrGetter<string>,
  matchId: MaybeRefOrGetter<string>,
) {
  const nameTagRef = computed(() => toValue(nameTag))
  const matchIdRef = computed(() => toValue(matchId))

  const key = computed(() => `match-detail-${nameTagRef.value}-${matchIdRef.value}`)

  const apiFetch = useApiFetch()

  const { data, status, error, refresh } = useLazyAsyncData<MatchDetail | null>(
    key,
    async (_nuxtApp, { signal }) => {
      if (!nameTagRef.value || !matchIdRef.value) return null

      try {
        const response = await apiFetch<MatchDetail | null>(
          `/truemains/${encodeURIComponent(nameTagRef.value)}/matches/${encodeURIComponent(matchIdRef.value)}`,
          { signal },
        )
        // A 2xx missing the participants array reads as a 404.
        return response && Array.isArray(response.participants) ? response : null
      }
      catch (err) {
        if (isNotFoundError(err)) return null
        throw err
      }
    },
    {
      server: false,
      watch: [nameTagRef, matchIdRef],
    },
  )

  // Derived from useAsyncData's own state, never written inside the handler —
  // the rule `useChampion` spells out for the same semantics: a `getCachedData`
  // short-circuits the handler on a cache hit, so a handler-set ref would go
  // stale across navigations (open a match the account never played, then a
  // cached one, and the flag would wrongly stick on `true`). There is no
  // `getCachedData` here today; deriving it is what keeps this correct if one
  // is ever added.
  const notFound = computed(() => data.value === null && status.value === 'success')

  return {
    data,
    isLoading: computed(() => isLoadingStatus(status.value)),
    notFound,
    error,
    refresh,
  }
}
