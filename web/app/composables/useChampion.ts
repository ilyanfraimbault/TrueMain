import type { ChampionResponse } from '~~/shared/types/champions'
import { fetchErrorStatus } from '~/utils/errors'

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
 * brand-new champion, or one nobody in the dataset has played). Either way it
 * surfaces as `notEnoughData = true` (with `data` left null) instead of
 * throwing, so the page can render a clear "no data" empty state rather than a
 * generic, retry-looking failure. The global variant still retries once
 * without filters first: a 404 on a filtered slice usually just means that
 * patch/position is empty, so we fall back to the champion's default slice and
 * only treat it as "no data" when even the unfiltered fetch 404s.
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

  const notEnoughData = ref(false)

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
      notEnoughData.value = false

      if (nameTag) {
        try {
          return await $fetch<ChampionResponse>(
            `/api/truemains/${encodeURIComponent(nameTag)}/champions/${id}`,
            { query: f },
          )
        }
        catch (error: unknown) {
          // The controller returns 404 for an unknown player or a champion
          // below the min-games floor — that's an empty state, not an error,
          // so swallow it into `notEnoughData`. Every other status (429, 500,
          // problem responses) is a real failure and must propagate so the
          // page can surface it (inline alert + toast) instead of pretending
          // the player simply hasn't played enough.
          if (fetchErrorStatus(error) === 404) {
            notEnoughData.value = true
            return null
          }
          throw error
        }
      }

      try {
        return await $fetch<ChampionResponse>(`/api/champions/${id}`, { query: f })
      }
      catch (error: unknown) {
        // Anything other than a 404 (429, 500, network, problem responses) is a
        // real failure and must propagate so the page can surface it.
        if (fetchErrorStatus(error) !== 404) throw error

        const hadFilters = Boolean(f.patch || f.position)
        if (hadFilters) {
          // A 404 on a filtered slice usually just means that patch/position is
          // empty — retry the champion's default slice before concluding there's
          // no data at all.
          try {
            const fallback = await $fetch<ChampionResponse>(`/api/champions/${id}`)
            // Stash this default slice under the unfiltered key. When the page's
            // URL reconciler clears the dead filter, the data key flips from the
            // filtered key to `buildKey('', '')`; `getCachedData` below then
            // reuses this identical response instead of triggering a second
            // no-filter fetch (and its loading flash).
            nuxtApp.static.data[unfilteredKey] = fallback
            return fallback
          }
          catch (fallbackError: unknown) {
            if (fetchErrorStatus(fallbackError) !== 404) throw fallbackError
            // Even the unfiltered fetch 404s: we hold no aggregate for this
            // champion at all. Fall through to the no-data empty state.
          }
        }

        // A 404 with no filters (or whose unfiltered fallback also 404s) means
        // we genuinely have no data on this champion yet — an empty state, not
        // an error, so swallow it into `notEnoughData` instead of throwing.
        notEnoughData.value = true
        return null
      }
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

  return { ...result, notEnoughData }
}
