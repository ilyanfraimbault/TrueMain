import type { ChampionMatchup } from '~~/shared/types/champions'
import type { ChampionPosition } from '~/utils/positions'

export interface UseChampionMatchupOptions {
  /** Patch to scope the matchup to (major.minor). Omit for all patches. */
  patch?: MaybeRefOrGetter<string | null | undefined>
  /**
   * When provided, the matchup is scoped to a single player via
   * `GET /api/truemains/{nameTag}/champions/{id}/matchup` instead of the
   * global `GET /api/champions/{id}/matchup`. Same contract, different source.
   */
  nameTag?: MaybeRefOrGetter<string | undefined>
}

/**
 * Fetches one lane-matchup slice — how `championId` performs at `position`
 * against `opponentId` in the same lane. Only fires once both a position and
 * an opponent are set; before that it resolves to null so the card can show a
 * prompt. A 404 means the matchup has fewer than the configured minimum games,
 * surfaced as `notEnoughData` (with `data` left null) rather than an error.
 */
export function useChampionMatchup(
  championId: MaybeRefOrGetter<number>,
  position: MaybeRefOrGetter<ChampionPosition | null>,
  opponentId: MaybeRefOrGetter<number | null>,
  options: UseChampionMatchupOptions = {},
) {
  const championIdRef = computed(() => toValue(championId))
  const positionRef = computed(() => toValue(position))
  const opponentIdRef = computed(() => toValue(opponentId))
  const patchRef = computed(() => {
    const value = toValue(options.patch)
    return value && value.length > 0 ? value : undefined
  })
  const nameTagRef = computed(() => {
    const value = toValue(options.nameTag)
    return value && value.length > 0 ? value : undefined
  })

  const notEnoughData = ref(false)

  const result = useLazyAsyncData<ChampionMatchup | null>(
    () => [
      'champion-matchup',
      nameTagRef.value ?? 'global',
      championIdRef.value,
      positionRef.value ?? '',
      opponentIdRef.value ?? '',
      patchRef.value ?? '',
    ].join('-'),
    async () => {
      notEnoughData.value = false

      const position = positionRef.value
      const opponent = opponentIdRef.value
      // No selection yet — resolve to null without touching the network.
      if (!position || !opponent) {
        return null
      }

      const query: Record<string, string> = {
        position,
        opponentId: String(opponent),
      }
      if (patchRef.value) query.patch = patchRef.value

      const nameTag = nameTagRef.value
      const path = nameTag
        ? `/api/truemains/${encodeURIComponent(nameTag)}/champions/${championIdRef.value}/matchup`
        : `/api/champions/${championIdRef.value}/matchup`

      try {
        return await $fetch<ChampionMatchup>(path, { query })
      }
      catch (error: unknown) {
        const status = (error as { statusCode?: number, response?: { status?: number } }).statusCode
          ?? (error as { response?: { status?: number } }).response?.status
        // A 404 is the "too few games / unknown player" signal — an empty
        // state, not a failure. Anything else (400 / 429 / 5xx) is a real
        // error: rethrow so the card can surface it instead of masking it as
        // "not enough data".
        if (status === 404) {
          notEnoughData.value = true
          return null
        }
        throw error
      }
    },
    {
      watch: [championIdRef, positionRef, opponentIdRef, patchRef, nameTagRef],
      server: false,
    },
  )

  return { ...result, notEnoughData }
}
