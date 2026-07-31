import type { ChampionPowerspikesResponse } from '~~/shared/types/champions'

/**
 * Event spikes for one core build of the champion (issues #571, scoped per core
 * build in #890).
 *
 * Unlike the sibling detail slices this one is not built on
 * {@link createChampionPatchSlice}: spikes are only meaningful within a single
 * build, so the request carries the build key on top of the shared
 * (champion, position, patch, elo) scope, and the API rejects a call without it.
 * The key includes the build pair so every build tab caches separately.
 *
 * `opponentChampionId` re-slices the spikes against the lane opponent picked in
 * the filter bar (#957), the same scope the build sections already carry. It is
 * part of the key too, so clearing the opponent falls back to the cached global
 * slice instead of refetching it.
 */
export function useChampionPowerspikes(
  championId: MaybeRefOrGetter<number>,
  position: MaybeRefOrGetter<string | null | undefined>,
  patch: MaybeRefOrGetter<string | null | undefined>,
  buildFirstItemId: MaybeRefOrGetter<number | null | undefined>,
  buildKeystoneId: MaybeRefOrGetter<number | null | undefined>,
  eloBracket: MaybeRefOrGetter<string | null | undefined> = undefined,
  opponentChampionId: MaybeRefOrGetter<number | null | undefined> = undefined,
) {
  const championIdRef = computed(() => toValue(championId))
  const positionRef = computed(() => toValue(position) || undefined)
  const patchRef = computed(() => toValue(patch) || undefined)
  const firstItemRef = computed(() => toValue(buildFirstItemId) || 0)
  const keystoneRef = computed(() => toValue(buildKeystoneId) || 0)
  const eloBracketRef = computed(() => toValue(eloBracket) || undefined)
  const opponentRef = computed(() => toValue(opponentChampionId) || 0)

  const empty = (): ChampionPowerspikesResponse => ({
    championId: championIdRef.value,
    position: positionRef.value ?? '',
    patch: patchRef.value ?? null,
    events: [],
  })

  return useLazyAsyncData<ChampionPowerspikesResponse>(
    () => `champion-powerspikes|${championIdRef.value}|${positionRef.value ?? ''}|${patchRef.value ?? ''}`
      + `|${eloBracketRef.value ?? ''}|${firstItemRef.value}|${keystoneRef.value}|${opponentRef.value}`,
    () => {
      // The build key is required server-side, so hold rather than fire a 400
      // while the build (or the lane) is still resolving.
      if (!positionRef.value || firstItemRef.value <= 0 || keystoneRef.value <= 0) {
        return Promise.resolve(empty())
      }

      const query: Record<string, string | number> = {
        position: positionRef.value,
        buildFirstItemId: firstItemRef.value,
        buildKeystoneId: keystoneRef.value,
      }
      if (patchRef.value) query.patch = patchRef.value
      if (eloBracketRef.value) query.eloBracket = eloBracketRef.value
      if (opponentRef.value > 0) query.opponentChampionId = opponentRef.value

      return $fetch<ChampionPowerspikesResponse>(
        `/api/champions/${championIdRef.value}/powerspikes`,
        { query },
      )
    },
    {
      watch: [championIdRef, positionRef, patchRef, firstItemRef, keystoneRef, eloBracketRef, opponentRef],
      server: false,
    },
  )
}
