<script setup lang="ts">
import type { LeaderboardSort, RegionSlug } from '~~/shared/types/leaderboard'
import type { ChampionPosition } from '~/utils/positions'

// Position anchors the left edge and region the right edge; OTP + sort sit
// in a `flex-1 justify-center` middle group so they're centered in the gap
// between the two rather than crowding next to the position picker. Champion
// filtering lives in the page's top AppSearch bar, not here. Each filter has
// its own reset affordance: the position picker's "All" button and the
// region select's "All regions" entry. There's no global Clear button —
// per-field clearing is faster and avoids the nuclear option for a single
// mis-click.
const props = defineProps<{
  region: RegionSlug | null
  position: ChampionPosition | null
  otpOnly: boolean
  /** Active ranking column — drives the "Sort by" select below. */
  sort: LeaderboardSort
}>()

const emit = defineEmits<{
  'update:region': [value: RegionSlug | null]
  'update:position': [value: ChampionPosition | null]
  'update:otpOnly': [value: boolean]
  'update:sort': [value: LeaderboardSort]
}>()

interface SortItem {
  label: string
  value: LeaderboardSort
  icon: string
  /** Rendered under the label in the select dropdown (SelectMenu's `descriptionKey`). */
  description: string
}

const SORT_OPTIONS: SortItem[] = [
  {
    label: 'LP',
    value: 'rank',
    icon: 'i-lucide-trophy',
    description: 'Rank by current ranked standing (tier, then LP)',
  },
  {
    label: 'Dedication',
    value: 'dedication',
    icon: 'i-lucide-heart',
    description: 'Rank by dedication to the signature champion (share of games, patches played, volume, recency)',
  },
]

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

const selectedSort = computed<SortItem>(() =>
  SORT_OPTIONS.find(o => o.value === props.sort) ?? SORT_OPTIONS[0]!)

function onSortChange(item: SortItem | undefined) {
  emit('update:sort', item?.value ?? SORT_OPTIONS[0]!.value)
}
</script>

<template>
  <div class="flex flex-wrap items-center gap-3">
    <!-- Position: narrowest filter, anchors the left edge. Reuses the same
         component the /champions page uses so the two filter strips feel
         identical. -->
    <RolePicker
      :position="position"
      @update:position="value => emit('update:position', value)"
    />

    <!-- OTP + sort centered in the gap between position and region. -->
    <div class="flex flex-1 flex-wrap items-center justify-center gap-3">
      <!-- OTP-only toggle. Amber when active to echo the row's OTP badge; a
           pressed button rather than a switch so it matches the RolePicker's
           button-strip affordance. `aria-pressed` exposes the toggle state. -->
      <UButton
        :color="otpOnly ? 'warning' : 'neutral'"
        :variant="otpOnly ? 'soft' : 'ghost'"
        size="sm"
        icon="i-lucide-user-check"
        :aria-pressed="otpOnly"
        title="Show only one-trick ponies (≥85% on a single champion)"
        @click="emit('update:otpOnly', !otpOnly)"
      >
        OTP only
      </UButton>

      <!-- Ranking column: a "Sort by" label + select rather than a bare
           icon/label toggle pair, so the control's purpose reads on its own
           instead of relying on a hover title. Each option's description
           shows in the dropdown (SelectMenu's default `descriptionKey`).
           Same USelectMenu pattern as the region filter below. -->
      <div class="flex items-center gap-2">
        <span class="text-sm text-muted">Sort by</span>
        <USelectMenu
          :model-value="selectedSort"
          :items="SORT_OPTIONS"
          :search-input="false"
          :ui="{ content: 'w-64' }"
          class="w-40"
          @update:model-value="onSortChange"
        >
          <template #leading>
            <UIcon :name="selectedSort.icon" class="size-[18px]" />
          </template>
          <template #item-leading="{ item }">
            <UIcon :name="(item as SortItem).icon" class="size-[18px]" />
          </template>
        </USelectMenu>
      </div>
    </div>

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
