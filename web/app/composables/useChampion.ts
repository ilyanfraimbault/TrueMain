import type { ChampionResponse } from '~~/shared/types/champions'
import { fetchErrorStatus } from '~/utils/errors'
import { resolveGlobalChampion } from '~/utils/champion-fetch'

type Filters = ReturnType<typeof useChampionFilters>['filters']

export interface UseChampionOptions {
  /**
   * When provided, the champion aggregate is scoped to a single player via
   * `GET /api/truemains/{nameTag}/champions/{id}` instead of the global
   * `GET /api/champions/{id}`. The read model is identical, so the rest of
   * the page is unchanged — only the data source differs.
   */
  nameTag?: MaybeRefOrGetter<string | undefined>
}

/**
 * Fetches the champion build page payload. Global by default; pass
 * `options.nameTag` to scope every aggregate to that player's games on the
 * champion (used by `/truemains/{nameTag}/champions/{id}`).
 *
 * A 404 is always meaningful — for the player-scoped variant it means the
 * account is unknown or the player has too few games on the champion; for the
 * global variant it means we simply hold no aggregate for that champion yet (a
 * brand-new champion, or one nobody in the dataset has played). Either way the
 * handler resolves to `data = null` (instead of throwing), and the returned
 * `notEnoughData` flag surfaces that so the page can render a clear "no data"
 * empty state rather than a generic, retry-looking failure. The global variant
 * still retries once without filters first: a 404 on a filtered slice usually
 * just means that patch/position is empty, so we fall back to the champion's
 * default slice and only treat it as "no data" when even the unfiltered fetch
 * 404s.
 *
 * `notEnoughData` is derived from useAsyncData's own state (`data === null` on a
 * settled fetch), NOT set imperatively inside the handler: `getCachedData`
 * short-circuits the handler on a cache hit, so a handler-set ref would go
 * stale across navigations (e.g. open a no-data champion, then a cached one —
 * the flag would wrongly stick on `true`).
 */
export function useChampion(
  championId: MaybeRefOrGetter<number>,
  filters: Filters,
  options: UseChampionOptions = {},
) {
  const nuxtApp = useNuxtApp()

  const championIdRef = computed(() => toValue(championId))
  const nameTagRef = computed(() => {
    const value = toValue(options.nameTag)
    return value && value.length > 0 ? value : undefined
  })

  const buildKey = (patch: string, position: string) =>
    ['champion', nameTagRef.value ?? 'global', championIdRef.value, patch, position].join('-')

  const result = useLazyAsyncData<ChampionResponse | null>(
    () => {
      const f = filters.value
      return buildKey(f.patch ?? '', f.position ?? '')
    },
    async () => {
      const id = championIdRef.value
      const f = filters.value
      const nameTag = nameTagRef.value
      // Capture the stash key synchronously, before any `await`: if the user
      // navigates to another champion while a fetch is in flight, the refs
      // change underneath us and `buildKey` would otherwise stash this
      // response under the new champion's key.
      const unfilteredKey = buildKey('', '')

      if (nameTag) {
        try {
          return await $fetch<ChampionResponse>(
            `/api/truemains/${encodeURIComponent(nameTag)}/champions/${id}`,
            { query: f },
          )
        }
        catch (error: unknown) {
          // The controller returns 404 for an unknown player or a champion
          // below the min-games floor — that's an empty state, not an error, so
          // resolve to null (surfaced as `notEnoughData`). Every other status
          // (429, 500, problem responses) is a real failure and must propagate
          // so the page can surface it (inline alert + toast) instead of
          // pretending the player simply hasn't played enough.
          if (fetchErrorStatus(error) === 404) return null
          throw error
        }
      }

      // Global variant: a 404 means "no data for this champion" rather than an
      // error. resolveGlobalChampion runs the fetch + unfiltered fallback and
      // classifies the result (null = no data); non-404 failures still throw.
      // Stash any fallback slice under the unfiltered key so that when the
      // page's URL reconciler clears a dead filter (flipping the data key to
      // `buildKey('', '')`) `getCachedData` reuses this identical response
      // instead of triggering a second no-filter fetch (and its loading flash).
      const outcome = await resolveGlobalChampion(
        query => $fetch<ChampionResponse>(`/api/champions/${id}`, { query }),
        f,
      )
      if (outcome.fallbackData) nuxtApp.static.data[unfilteredKey] = outcome.fallbackData
      return outcome.data
    },
    {
      watch: [championIdRef, nameTagRef, filters],
      server: false,
      // Dedupe only the global, unfiltered slice: after the 404 fallback has
      // already fetched (and stashed) the default slice, reuse it when the key
      // flips to the unfiltered one. Filtered keys and the player-scoped
      // (nameTag) variant always fetch, so their behaviour is unchanged.
      getCachedData: (key, _app, ctx) => {
        if (nameTagRef.value || key !== buildKey('', '')) return undefined
        // Mirror Nuxt's default getter: never short-circuit an explicit
        // refresh, only the watch/key-change reload the reconciler triggers.
        if (ctx?.cause === 'refresh:manual' || ctx?.cause === 'refresh:hook') return undefined
        // Prefer our stash; fall through only when the key is genuinely absent
        // (an `in` check, not `??`, so a stashed `null` wouldn't be mistaken for
        // a miss should the slice type ever allow it).
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

  return { ...result, notEnoughData }
}
