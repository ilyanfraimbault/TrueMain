import type { ChampionRoamResponse } from '~~/shared/types/champions'

/**
 * Roam metric (per-game out-of-lane kill participations at @5/@10/@15) for
 * the champion detail page (issue #536). Forwards position + the pinned
 * patch, with the shared key/gating contract from
 * {@link createChampionPatchSlice}.
 */
export const useChampionRoam = createChampionPatchSlice<ChampionRoamResponse>({
  keyPrefix: 'champion-roam',
  endpoint: 'roam',
  emptyModel: (championId, position, patch) => ({
    championId,
    position,
    patch,
    games: 0,
    roamKp5: null,
    roamKp10: null,
    roamKp15: null,
  }),
})
