// Shared constants for the filter selects across the admin panels.
import { TRACKED_REGION_ITEMS } from '~~/shared/utils/regions'

// Reka UI forbids an empty-string SelectItem value, so "All …" options use the
// non-empty `'all'` sentinel; each panel's filter getters map it back to
// `undefined` (param omitted) so the backend still sees "no filter".
export const ALL = 'all'

// Tracked Riot regions for the region filter selects (champions, candidates,
// accounts, seed queue), behind the "all" sentinel. The platforms themselves
// come from `shared/utils/regions`, so adding a shard doesn't have to be
// remembered here as well as in the bulk-seed parser.
export const REGION_ITEMS = [
  { label: 'All regions', value: ALL },
  ...TRACKED_REGION_ITEMS,
]
