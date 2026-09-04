import type { ChampionItemContextResponse } from '~~/shared/types/item-context'

/**
 * The situational build context of the displayed slice (#1451).
 *
 * Deliberately not built on {@link createChampionPatchSlice}, which forwards
 * `eloBracket`: the verdicts carry no rank dimension, so sending one would be a dead
 * parameter that reads — in the network tab and to the next person here — as if the
 * page's rank filter reached this section. It does not, and the card says so.
 *
 * `server: false` like every other build panel: the champion page's SSR payload is the
 * build summary alone (#149), and this is hover content nobody sees before hydration.
 */
export function useChampionItemContext(
  championId: MaybeRefOrGetter<number>,
  position: MaybeRefOrGetter<string | null | undefined>,
  patch: MaybeRefOrGetter<string | null | undefined>,
) {
  const championIdRef = computed(() => toValue(championId))
  const positionRef = computed(() => toValue(position) || undefined)
  const patchRef = computed(() => toValue(patch) || undefined)

  const empty = (): ChampionItemContextResponse => ({
    championId: championIdRef.value,
    position: positionRef.value ?? '',
    patch: patchRef.value ?? null,
    allRanks: true,
    items: [],
  })

  return useLazyAsyncData<ChampionItemContextResponse>(
    () => `champion-item-context|${championIdRef.value}|${positionRef.value ?? ''}|${patchRef.value ?? ''}`,
    () => {
      // The lane is required server-side, so hold rather than fire a 400 while the
      // champion page is still resolving which lane it is showing.
      if (!championIdRef.value || !positionRef.value) {
        return Promise.resolve(empty())
      }

      const query: Record<string, string> = { position: positionRef.value }
      if (patchRef.value) query.patch = patchRef.value

      return $fetch<ChampionItemContextResponse>(
        `/api/champions/${championIdRef.value}/item-context`,
        { query },
      )
    },
    {
      watch: [championIdRef, positionRef, patchRef],
      server: false,
      default: empty,
    },
  )
}
