import type { ChampionTimelineLeadsResponse } from '~~/shared/types/champions'

/**
 * Per-interval average lead vs the lane opponent for the champion detail
 * page.
 *
 * Forwards both the position and the pinned patch — unlike the trend chart,
 * the leads slice is patch-scoped, so the active patch filter narrows it.
 * Key/gating contract shared via {@link createChampionPatchSlice}.
 */
export const useChampionTimelineLeads = createChampionPatchSlice<ChampionTimelineLeadsResponse>({
  keyPrefix: 'champion-timeline-leads',
  endpoint: 'timeline-leads',
  emptyModel: (championId, position, patch) => ({
    championId,
    position,
    patch,
    intervals: [],
  }),
})
