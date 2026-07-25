import type { ChampionMainsComparison } from '~~/shared/types/champions'
import type { ChampionPosition } from '~/utils/positions'
import { fetchErrorStatus } from '~/utils/errors'

export interface UseChampionMainsComparisonOptions {
  /**
   * Riot ID of a specific main to compare against (`Name#TAG`). Omitted, the
   * comparison uses the aggregate of every tracked main of the champion.
   */
  mainRiotId?: MaybeRefOrGetter<string | null | undefined>
  /** Lane both sides are narrowed to. Null compares across every lane. */
  position?: MaybeRefOrGetter<ChampionPosition | null | undefined>
}

/**
 * Head-to-head between a Riot account and a champion's mains (#528). Idle until
 * a Riot ID is supplied — there is nothing to fetch before the user names an
 * account — and purely client-side: the account only exists once typed.
 *
 * The endpoint answers 200 for every resolvable request, including for an
 * account we have never ingested (`status: 'UNKNOWN_ACCOUNT'`), so the caller
 * branches on `status` rather than on an error. A malformed Riot ID is the only
 * 400, and it resolves to null here so the panel can show its own hint instead
 * of an error toast.
 *
 * Deliberately not patch-scoped: retention already limits stored participants
 * to the recent patches, and pinning one on top would shred an already thin
 * per-player sample.
 */
export function useChampionMainsComparison(
  championId: MaybeRefOrGetter<number>,
  riotId: MaybeRefOrGetter<string | null | undefined>,
  options: UseChampionMainsComparisonOptions = {},
) {
  const championIdRef = computed(() => toValue(championId))
  const riotIdRef = computed(() => {
    const value = toValue(riotId)?.trim()
    return value ? value : null
  })
  const mainRiotIdRef = computed(() => {
    const value = toValue(options.mainRiotId)?.trim()
    return value ? value : null
  })
  const positionRef = computed(() => toValue(options.position) ?? null)

  return useLazyAsyncData<ChampionMainsComparison | null>(
    () => [
      'champion-mains-comparison',
      championIdRef.value,
      riotIdRef.value ?? '',
      mainRiotIdRef.value ?? '',
      positionRef.value ?? '',
    ].join('-'),
    async () => {
      const account = riotIdRef.value
      if (!account) return null

      const query: Record<string, string> = { account }
      if (mainRiotIdRef.value) query.main = mainRiotIdRef.value
      if (positionRef.value) query.position = positionRef.value

      try {
        return await $fetch<ChampionMainsComparison>(
          `/api/champions/${championIdRef.value}/mains-comparison`,
          { query },
        )
      }
      catch (error: unknown) {
        // A Riot ID the backend can't parse → the panel's own "use Name#TAG"
        // hint, not an error toast. Anything else propagates.
        if (fetchErrorStatus(error) === 400) return null
        throw error
      }
    },
    {
      watch: [championIdRef, riotIdRef, mainRiotIdRef, positionRef],
      server: false,
    },
  )
}
