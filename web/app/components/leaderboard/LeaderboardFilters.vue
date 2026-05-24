<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { RegionSlug } from '~~/shared/types/leaderboard'
import type { ChampionPosition } from '~/utils/positions'

// Three independent filters, ordered visually as: position (left) → search
// (middle, centred via mx-auto) → region (right). Each filter has its own
// reset affordance: the position picker's "All" button, the champion
// picker's inline X, and the region select's "All regions" entry. There's
// no global Clear button — per-field clearing is faster and avoids the
// nuclear option for a single mis-click.
const props = defineProps<{
  champions: ChampionStaticListItem[]
  region: RegionSlug | null
  position: ChampionPosition | null
  championId: number | null
}>()

const emit = defineEmits<{
  'update:region': [value: RegionSlug | null]
  'update:position': [value: ChampionPosition | null]
  'update:championId': [value: number | null]
}>()

interface RegionItem {
  label: string
  value: RegionSlug | null
}

// `null` value = "All regions" — keeps the select tri-state without
// needing an extra affordance to clear the field.
const REGION_OPTIONS: RegionItem[] = [
  { label: 'All regions', value: null },
  { label: 'Europe', value: 'europe' },
  { label: 'Americas', value: 'americas' },
  { label: 'Korea', value: 'korea' },
]

const selectedRegion = computed<RegionItem>(() =>
  REGION_OPTIONS.find(o => o.value === props.region) ?? REGION_OPTIONS[0]!)

function onRegionChange(item: RegionItem | undefined) {
  emit('update:region', item?.value ?? null)
}
</script>

<template>
  <div class="flex flex-wrap items-center gap-3">
    <!-- Position: leftmost, narrowest filter. Reuses the same component
         the /champions page uses so the two filter strips feel identical. -->
    <RolePicker
      :position="position"
      @update:position="value => emit('update:position', value)"
    />

    <!-- Champion search: middle slot, mx-auto centres it in the space
         left over by the position pills (left) and the region select
         (right). The inline X (built into ChampionPicker) clears the
         selection without touching the other filters. -->
    <ChampionPicker
      :champions="champions"
      :champion-id="championId"
      placeholder="Search for a champion"
      trigger-class="mx-auto w-64"
      @update:champion-id="value => emit('update:championId', value)"
    />

    <!-- Region: rightmost, single dropdown so the strip stays compact and
         each region is reachable in one click. The flag renders in both
         the trigger and the option rows via USelectMenu slots. -->
    <USelectMenu
      :model-value="selectedRegion"
      :items="REGION_OPTIONS"
      class="w-40"
      @update:model-value="onRegionChange"
    >
      <template #leading>
        <LeaderboardRegionFlag :region="selectedRegion.value" :width="18" />
      </template>
      <template #item-leading="{ item }">
        <LeaderboardRegionFlag :region="(item as RegionItem).value" :width="18" />
      </template>
    </USelectMenu>
  </div>
</template>
