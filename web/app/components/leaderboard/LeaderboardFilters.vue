<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { RegionSlug } from '~~/shared/types/leaderboard'
import type { ChampionPosition } from '~/utils/positions'

// Three independent filters, ordered visually as: position (left) → champion
// chip (middle, only present once a champion is active) → region (right).
// Each filter has its own reset affordance: the position picker's "All"
// button, the champion chip's X, and the region select's "All regions"
// entry. There's no global Clear button — per-field clearing is faster and
// avoids the nuclear option for a single mis-click.
//
// The champion filter itself is only ever *set* here — it comes from
// AppSearch's unified search, which by design can't clear it (see the note
// in AppSearch.vue). This chip exists purely to show what's active and clear
// it back to "all champions".
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

const filteredChampion = computed(() =>
  props.championId === null
    ? null
    : props.champions.find(c => c.championId === props.championId) ?? null,
)

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

    <!-- Active champion filter: middle slot, mx-auto centres it when
         present. Read-only chip (the search bar is what sets it) — clicking
         it clears the filter back to "all champions". -->
    <button
      v-if="filteredChampion"
      type="button"
      class="glass-hover mx-auto flex items-center gap-1.5 rounded-full border border-default/60 bg-elevated/40 py-1 pl-1.5 pr-2.5 text-sm"
      @click="emit('update:championId', null)"
    >
      <SkeletonImage
        :src="filteredChampion.iconUrl"
        :alt="filteredChampion.name"
        width="20"
        height="20"
        class="size-5 rounded-full"
      />
      <span class="max-w-[8rem] truncate">{{ filteredChampion.name }}</span>
      <UIcon
        name="i-lucide-x"
        class="size-3.5 text-dimmed"
      />
    </button>

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
