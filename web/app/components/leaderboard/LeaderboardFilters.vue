<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { RegionSlug } from '~~/shared/types/leaderboard'
import type { ChampionPosition } from '~/utils/positions'

// Three independent filters, ordered visually as: position (left) → search
// (middle, grows with the viewport) → region (right). The champion picker
// is the most-used filter so it sits in the central slot people gravitate
// to.
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
  // Single event for the "Clear" button — three `update:*` emits in a row
  // would race against the parent's debounced router.replace and the last
  // call could overwrite the earlier filter clears.
  'clear': []
}>()

interface RegionItem {
  label: string
  value: RegionSlug | null
}

// `null` value = "All regions" — keeps the select tri-state without
// needing an extra Clear affordance just for region.
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

function clearAll() {
  emit('clear')
}

const hasAnyFilter = computed(() =>
  props.region !== null || props.position !== null || props.championId !== null)
</script>

<template>
  <div class="flex flex-wrap items-center gap-3">
    <!-- Position: leftmost, narrowest filter -->
    <RolePicker
      :position="position"
      @update:position="value => emit('update:position', value)"
    />

    <!-- Champion search: middle slot. `mx-auto` on a single flex item
         pushes equal auto margin on both sides, centring the picker in the
         space left over by the position pills (left) and the region select
         (right). -->
    <ChampionPicker
      :champions="champions"
      :champion-id="championId"
      placeholder="Search for a champion"
      trigger-class="mx-auto w-64"
      @update:champion-id="value => emit('update:championId', value)"
    />

    <!-- Region: rightmost, single dropdown so the strip stays compact and
         each region is reachable in one click. The flag renders in both the
         trigger and the option rows via USelectMenu slots. -->
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

    <UButton
      v-if="hasAnyFilter"
      variant="ghost"
      color="neutral"
      size="xs"
      icon="i-lucide-x"
      @click="clearAll"
    >
      Clear
    </UButton>
  </div>
</template>
