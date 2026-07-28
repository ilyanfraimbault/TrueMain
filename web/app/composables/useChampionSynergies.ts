import type { ChampionSynergies, ChampionTrioSynergies } from '~~/shared/types/champions'
import type { ChampionPosition } from '~/utils/positions'

export interface UseChampionSynergiesOptions {
  /**
   * Narrow to a single partner lane. Sent as `?partnerPosition=<value>`; null
   * asks for every lane. Only the returned list changes — the cohort reference
   * point behind each synergy is a property of the scope, not of the filter.
   */
  partnerPosition?: MaybeRefOrGetter<ChampionPosition | null | undefined>
  /** Elo filter (exact tier or cumulative "X+" threshold). Sent as `?eloBracket=`. */
  eloBracket?: MaybeRefOrGetter<string | null | undefined>
  /** Patch (`major.minor`); omitted means every patch the aggregate holds. */
  patch?: MaybeRefOrGetter<string | null | undefined>
}

/**
 * Fetches a champion's best duo partners at a lane, ranked by synergy rather
 * than by raw pair win rate. Fires once a position is known.
 */
export function useChampionSynergies(
  championId: MaybeRefOrGetter<number>,
  position: MaybeRefOrGetter<ChampionPosition | null>,
  options: UseChampionSynergiesOptions = {},
) {
  const championIdRef = computed(() => toValue(championId))
  const positionRef = computed(() => toValue(position))
  const partnerPositionRef = computed(() => toValue(options.partnerPosition) || undefined)
  const eloBracketRef = computed(() => toValue(options.eloBracket) || undefined)
  const patchRef = computed(() => toValue(options.patch) || undefined)

  return useLazyAsyncData<ChampionSynergies | null>(
    () => [
      'champion-synergies',
      championIdRef.value,
      positionRef.value ?? '',
      partnerPositionRef.value ?? '',
      eloBracketRef.value ?? '',
      patchRef.value ?? '',
    ].join('-'),
    async () => {
      const position = positionRef.value
      if (!position) return null

      const query: Record<string, string> = { position }
      if (partnerPositionRef.value) query.partnerPosition = partnerPositionRef.value
      if (eloBracketRef.value) query.eloBracket = eloBracketRef.value
      if (patchRef.value) query.patch = patchRef.value

      return await $fetch<ChampionSynergies>(
        `/api/champions/${championIdRef.value}/synergies`,
        { query },
      )
    },
    {
      watch: [championIdRef, positionRef, partnerPositionRef, eloBracketRef, patchRef],
      server: false,
    },
  )
}

/**
 * Fetches the third picks for an already-chosen duo. Idle until a partner is
 * selected — the endpoint is a live join, so it is never fired speculatively.
 */
export function useChampionTrioSynergies(
  championId: MaybeRefOrGetter<number>,
  position: MaybeRefOrGetter<ChampionPosition | null>,
  partnerChampionId: MaybeRefOrGetter<number | null>,
  partnerPosition: MaybeRefOrGetter<string | null>,
  options: Omit<UseChampionSynergiesOptions, 'partnerPosition'> = {},
) {
  const championIdRef = computed(() => toValue(championId))
  const positionRef = computed(() => toValue(position))
  const partnerRef = computed(() => toValue(partnerChampionId))
  const partnerPositionRef = computed(() => toValue(partnerPosition))
  const eloBracketRef = computed(() => toValue(options.eloBracket) || undefined)
  const patchRef = computed(() => toValue(options.patch) || undefined)

  return useLazyAsyncData<ChampionTrioSynergies | null>(
    () => [
      'champion-trio-synergies',
      championIdRef.value,
      positionRef.value ?? '',
      partnerRef.value ?? '',
      partnerPositionRef.value ?? '',
      eloBracketRef.value ?? '',
      patchRef.value ?? '',
    ].join('-'),
    async () => {
      const position = positionRef.value
      const partner = partnerRef.value
      const partnerPosition = partnerPositionRef.value
      // No duo picked yet — resolve to null rather than calling the live join.
      if (!position || partner == null || !partnerPosition) return null

      const query: Record<string, string> = {
        position,
        partner: String(partner),
        partnerPosition,
      }
      if (eloBracketRef.value) query.eloBracket = eloBracketRef.value
      if (patchRef.value) query.patch = patchRef.value

      return await $fetch<ChampionTrioSynergies>(
        `/api/champions/${championIdRef.value}/synergies/trios`,
        { query },
      )
    },
    {
      watch: [championIdRef, positionRef, partnerRef, partnerPositionRef, eloBracketRef, patchRef],
      server: false,
    },
  )
}
