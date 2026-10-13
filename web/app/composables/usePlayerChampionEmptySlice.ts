import { POSITION_BY_VALUE } from '#common/utils/positions'

type ChampionFilterState = ReturnType<typeof useChampionFilters>

/**
 * The player-scoped champion page's empty slice. The build filters keep working
 * when the slice they select is empty: a lane the player never played 404s the
 * aggregate (`notEnoughData`), and the header — its pickers included — still
 * renders over the zero-game slice, so the reader can undo a filter they set in
 * place.
 */
export function usePlayerChampionEmptySlice(options: {
  championId: ComputedRef<number>
  filters: ChampionFilterState['filters']
  setFilter: ChampionFilterState['setFilter']
  selectedPatch: ComputedRef<string>
}) {
  const { championId, filters, setFilter, selectedPatch } = options

  // Human-readable list of the filters currently narrowing the page, so the empty
  // notice can say what emptied it. Only the ones this page actually renders — it
  // has no rank or population control.
  const activeFilterLabels = computed(() => {
    const labels: string[] = []
    if (filters.value.patch) labels.push(filters.value.patch)
    const position = filters.value.position
    if (position) labels.push(POSITION_BY_VALUE.get(position)?.label ?? position)
    return labels
  })

  // The patch the API last resolved for this champion. `selectedPatch` falls back
  // to the loaded aggregate's patch, which goes null the moment a filter empties
  // the slice — blanking the patch select right when it has to be usable. Reset
  // on champion change so nothing leaks across a navigation on this same route.
  const lastResolvedPatch = ref('')
  watch(championId, () => { lastResolvedPatch.value = '' })
  watch(selectedPatch, (patch) => { if (patch) lastResolvedPatch.value = patch }, { immediate: true })
  const pickerPatch = computed(() => selectedPatch.value || lastResolvedPatch.value)

  // The position picker hides its "all positions" button (the API always answers
  // for one lane), so clearing is the only way back from an empty lane.
  function clearFilters() {
    void setFilter({ patch: null, position: null })
  }

  return { activeFilterLabels, pickerPatch, clearFilters }
}
