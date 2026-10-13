<script setup lang="ts">
// The filter row of the champion lists — the /champions directory and the tier
// list (#2032): lane, champion search, rank, population and patch, all read
// from and written to the URL through `useChampionFilters`, so moving between
// the two pages keeps every control in place.
import type { ChampionStaticListItem } from '#shared/types/static-data'
import { isChampionPosition, type ChampionPosition } from '#common/utils/positions'
import { normalizeEloBracket } from '#common/utils/elo-brackets'

const props = defineProps<{
  champions: ChampionStaticListItem[]
  /** The patch the list's data came back for — shown when `?patch=` is absent. */
  apiPatch: string
  /** A paginated list: every filter change anchors back on page 1. */
  paginated?: boolean
}>()

const { filters, setFilter } = useChampionFilters()
const filterChampionId = useRouteQueryChampionId()
const { data: versions } = useDDragonVersions()

// null = "All positions" — the RolePicker contract.
const selectedPosition = computed<ChampionPosition | null>(() => {
  const value = filters.value.position ?? ''
  return isChampionPosition(value) ? value : null
})

// The composable always resolves a concrete bracket (Master+ by default), so
// the picker always reflects the threshold actually being fetched.
const selectedEloBracket = computed<string>(() => normalizeEloBracket(filters.value.eloBracket))

const selectedPatch = computed(() => filters.value.patch || props.apiPatch || '')
const patchOptions = usePatchOptions(versions, () => props.apiPatch, () => filters.value.patch)

// `resetPage` drops `?page=` in the same router.replace on a paginated list.
function update(updates: Parameters<typeof setFilter>[0]) {
  void setFilter(updates, { resetPage: props.paginated })
}

function onPatchChange(value: unknown) {
  if (typeof value !== 'string' || !value) return
  update({ patch: value })
}
</script>

<template>
  <!-- Position anchored left, champion search dead-center, rank + patch
       grouped right as compact secondary filters. The 1fr side columns keep
       the search centered regardless of how wide the flanks are; below md
       everything stacks. -->
  <div class="grid grid-cols-1 items-center gap-3 md:grid-cols-[1fr_auto_1fr]">
    <RolePicker
      class="justify-self-start"
      :position="selectedPosition"
      @update:position="value => update({ position: value })"
    />

    <ChampionPicker
      :champions="champions"
      :champion-id="filterChampionId"
      placeholder="Search for a champion"
      trigger-class="w-56"
      @update:champion-id="value => update({ championId: value })"
    />

    <div class="flex items-center gap-2 md:justify-self-end">
      <!-- The bracket goes through untouched — `null` would clear the param
           and land back on the Master+ default, making "All ranks"
           unselectable. -->
      <ChampionEloFilter
        size="sm"
        :model-value="selectedEloBracket"
        @update:model-value="value => update({ eloBracket: value })"
      />

      <ChampionTruemainToggle
        :model-value="filters.truemainsOnly"
        @update:model-value="value => update({ truemainsOnly: value })"
      />

      <USelect
        :model-value="selectedPatch || undefined"
        :items="patchOptions"
        placeholder="Patch"
        size="sm"
        class="w-20"
        @update:model-value="onPatchChange"
      />
    </div>
  </div>
</template>
