// Shared constants for the filter selects across the admin panels.

// Reka UI forbids an empty-string SelectItem value, so "All …" options use the
// non-empty `'all'` sentinel; each panel's filter getters map it back to
// `undefined` (param omitted) so the backend still sees "no filter".
export const ALL = 'all'

// Tracked Riot regions for the region filter selects (champions, candidates).
export const REGION_ITEMS = [
  { label: 'All regions', value: ALL },
  { label: 'EUW1', value: 'EUW1' },
  { label: 'KR', value: 'KR' },
  { label: 'NA1', value: 'NA1' },
]
