import type { ChampionPowerspikesResponse } from '~~/shared/types/champions'

/**
 * Power curve + event spikes for the champion detail page (issue #571).
 * Forwards position + the pinned patch (the slice is patch-scoped), with the
 * shared key/gating contract from {@link createChampionPatchSlice}.
 */
export const useChampionPowerspikes = createChampionPatchSlice<ChampionPowerspikesResponse>({
  keyPrefix: 'champion-powerspikes',
  endpoint: 'powerspikes',
  emptyModel: (championId, position, patch) => ({
    championId,
    position,
    patch,
    curve: [],
    events: [],
  }),
})
