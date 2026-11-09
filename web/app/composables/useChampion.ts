import type { ChampionResponse } from '~~/shared/types/champions'
import { describeStaleFetchError, fetchErrorStatus } from '#common/utils/errors'
import { resolveGlobalChampion } from '~/utils/champion-fetch'
import { GLOBAL_CHAMPION_SCOPE, type ChampionScope } from '#common/utils/champion-scope'

type Filters = ReturnType<typeof useChampionFilters>['filters']

/**
 * Fetches the champion build page payload for a scope: everyone's games
 * (`GET /api/champions/{id}`), or one account's
 * (`GET /api/truemains/{nameTag}/champions/{id}`). The read model is identical,
 * so the rest of the page is unchanged.
 *
 * A 404 is always meaningful — for one account it means the account is unknown
 * or has too few games on the champion; for everyone's games that we hold no
 * aggregate for the champion yet. Either way the handler resolves to
 * `data = null` (instead of throwing), and `notEnoughData` surfaces that so the
 * page renders a "no data" empty state rather than a retry-looking failure.
 * Everyone's games retry once without the auto-resolvable filters first
 * (`resolveGlobalChampion`): a 404 on a filtered slice usually just means that
 * patch/position is empty.
 *
 * `notEnoughData` is derived from useAsyncData's own state (`data === null` on a
 * settled fetch), NOT set imperatively inside the handler: `getCachedData`
 * short-circuits the handler on a cache hit, so a handler-set ref would go
 * stale across navigations.
 */
export function useChampion(
  championId: MaybeRefOrGetter<number>,
  filters: Filters,
  scope: MaybeRefOrGetter<ChampionScope> = GLOBAL_CHAMPION_SCOPE,
) {
  const nuxtApp = useNuxtApp()

  const championIdRef = computed(() => toValue(championId))
  const scopeRef = computed(() => toValue(scope))

  // One account's read takes the patch and the position only; everyone's also
  // the rank, the opponent and the population (#1346), each a different answer
  // to the same (patch, position), so each is part of its key. The 404 fallback
  // keeps the rank (and, with an opponent pinned, the opponent and its position),
  // so it stashes its slice under the key of exactly those filters.
  const playerKey = (nameTag: string, patch: string, position: string) =>
    ['champion', nameTag, championIdRef.value, patch, position].join('-')
  const globalKey = (patch: string, position: string, eloBracket: string, opponent: string, truemainsOnly: boolean) =>
    ['champion', 'global', championIdRef.value, patch, position, eloBracket, opponent, truemainsOnly ? 'truemains' : 'everyone'].join('-')
  const keyOf = (f: Filters['value']) => {
    const current = scopeRef.value
    if (current.kind === 'player') return playerKey(current.nameTag, f.patch ?? '', f.position ?? '')
    const opponent = f.opponentChampionId ? String(f.opponentChampionId) : ''
    return globalKey(f.patch ?? '', f.position ?? '', f.eloBracket ?? '', opponent, f.truemainsOnly)
  }
  const fallbackKeyOf = (f: Filters['value']) => f.opponentChampionId
    ? globalKey('', f.position ?? '', f.eloBracket ?? '', String(f.opponentChampionId), f.truemainsOnly)
    : globalKey('', '', f.eloBracket ?? '', '', f.truemainsOnly)

  const apiFetch = useApiFetch()

  const result = useAsyncData<ChampionResponse | null>(
    () => keyOf(filters.value),
    async (_nuxtApp, { signal }) => {
      const id = championIdRef.value
      const f = filters.value
      const current = scopeRef.value

      if (current.kind === 'player') {
        try {
          return await apiFetch<ChampionResponse>(
            `/truemains/${encodeURIComponent(current.nameTag)}/champions/${id}`,
            { query: { patch: f.patch, position: f.position }, signal },
          )
        }
        catch (error: unknown) {
          // An unknown player or a champion below the min-games floor is an
          // empty state; every other status is a real failure the page surfaces.
          if (fetchErrorStatus(error) === 404) return null
          throw error
        }
      }

      // Captured before any `await`: navigating to another champion mid-fetch
      // would otherwise stash this response under the new champion's key. When
      // the page's reconciler then clears the dead patch/position, the data key
      // flips to this one and `getCachedData` reuses the stash (no refetch).
      const fallbackKey = fallbackKeyOf(f)
      const outcome = await resolveGlobalChampion(
        query => apiFetch<ChampionResponse>(`/champions/${id}`, { query, signal }),
        f,
      )
      if (outcome.fallbackData !== null) nuxtApp.static.data[fallbackKey] = outcome.fallbackData
      return outcome.data
    },
    {
      watch: [championIdRef, scopeRef, filters],
      server: false,
      // Only everyone's slice at the rank's default patch/position is reused:
      // the stash the 404 fallback left, once the reconciler flips the key to it.
      getCachedData: (key, _app, ctx) => {
        if (scopeRef.value.kind === 'player' || key !== fallbackKeyOf(filters.value)) return undefined
        // Never short-circuit an explicit refresh, only the reconciler's key change.
        if (ctx?.cause === 'refresh:manual' || ctx?.cause === 'refresh:hook') return undefined
        const cached = key in nuxtApp.static.data ? nuxtApp.static.data[key] : nuxtApp.payload.data[key]
        return cached as ChampionResponse | null | undefined
      },
    },
  )

  // Derived, not imperative: a `null` payload on a settled (success) fetch is
  // the "no data" signal for both variants (the handler returns null on a 404
  // and throws on every other failure). Reading useAsyncData's own state keeps
  // this correct even when `getCachedData` skips the handler on a cache hit.
  const notEnoughData = computed(
    () => result.data.value === null && result.status.value === 'success',
  )

  // Not lazy, so this settles only once the first fetch has: a page that
  // awaits it in setup keeps the outgoing page under the loading bar on a
  // client-side navigation, and opens on its data rather than its skeleton
  // (#1689). `server: false` still defers the fetch past hydration, so a hard
  // load resolves this at once and keeps its skeleton.
  const ready = result.then(() => undefined)

  // A failed filter change keeps the previous slice on screen and answers with
  // a toast (#1668); only a failed first load is the page's inline alert.
  // Scoped to the champion (and player): another champion's build is never
  // drawn under this one's name.
  const actionToast = useActionToast()
  const { data, error, staleError } = useRefetchFallback(result, {
    scope: () => `${scopeRef.value.kind === 'player' ? scopeRef.value.nameTag : 'global'}-${championIdRef.value}`,
    onStaleFailure: failure => actionToast.failure('Could not update this champion', describeStaleFetchError(failure)),
  })

  return { ...result, data, error, staleError, notEnoughData, ready }
}
