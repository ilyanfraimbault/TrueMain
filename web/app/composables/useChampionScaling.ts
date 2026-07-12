import type { ChampionScalingResponse } from '~~/shared/types/champions'

/**
 * Win-rate-by-game-duration slice for the champion detail page (issue #537).
 *
 * Forwards both position and the pinned patch (the slice is patch-scoped),
 * with the shared key/gating contract from {@link createChampionPatchSlice}.
 */
export const useChampionScaling = createChampionPatchSlice<ChampionScalingResponse>({
  keyPrefix: 'champion-scaling',
  endpoint: 'scaling',
  emptyModel: (championId, position, patch) => ({
    championId,
    position,
    patch,
    buckets: [],
    scalingIndex: null,
  }),
})
