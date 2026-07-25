<script setup lang="ts">
import type { LeaderboardSort, RegionSlug } from '~~/shared/types/leaderboard'
import type { ChampionPosition } from '~/utils/positions'

// Two independent filters, ordered visually as: position (left) → region
// (right), pushed apart with justify-between. Champion filtering lives in the
// page's top AppSearch bar, not here. Each filter has its own reset
// affordance: the position picker's "All" button and the region select's
// "All regions" entry. There's no global Clear button — per-field clearing is
// faster and avoids the nuclear option for a single mis-click.
const props = defineProps<{
  region: RegionSlug | null
  position: ChampionPosition | null
  otpOnly: boolean
  /** Active ranking column — drives the LP / Dedication segmented control. */
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
  title: string
}

const SORT_OPTIONS: SortItem[] = [
  {
    label: 'LP',
    value: 'rank',
    icon: 'i-lucide-trophy',
    title: 'Rank by current ranked standing (tier, then LP)',
  },
  {
    label: 'Dedication',
    value: 'dedication',
    icon: 'i-lucide-heart',
    title: 'Rank by dedication to the signature champion (share of games, patches played, volume, recency)',
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
</script>

<template>
  <div class="flex flex-wrap items-center justify-between gap-3">
    <!-- Position + OTP toggle share the left cluster; region stays right. -->
    <div class="flex flex-wrap items-center gap-3">
      <!-- Position: narrowest filter. Reuses the same component the /champions
           page uses so the two filter strips feel identical. -->
      <RolePicker
        :position="position"
        @update:position="value => emit('update:position', value)"
      />

      <!-- OTP-only toggle. Amber when active to echo the row's OTP badge; a
           pressed button rather than a switch so it matches the RolePicker's
           button-strip affordance. `aria-pressed` exposes the toggle state. -->
      <UButton
        :color="otpOnly ? 'warning' : 'neutral'"
        :variant="otpOnly ? 'soft' : 'ghost'"
        size="sm"
        icon="i-lucide-target"
        :aria-pressed="otpOnly"
        title="Show only one-trick ponies (≥85% on a single champion)"
        @click="emit('update:otpOnly', !otpOnly)"
      >
        OTP only
      </UButton>

      <!-- Ranking column. A two-button segmented control rather than a select:
           there are exactly two orders, and both stay one click away. Emerald
           (primary) on the active side, matching the rest of the app's accent —
           the amber above is reserved for the OTP semantics. -->
      <div
        class="flex items-center gap-1 rounded-md p-0.5 ring-1 ring-default/60"
        role="group"
        aria-label="Sort the leaderboard"
      >
        <UButton
          v-for="option in SORT_OPTIONS"
          :key="option.value"
          :color="sort === option.value ? 'primary' : 'neutral'"
          :variant="sort === option.value ? 'soft' : 'ghost'"
          size="sm"
          :icon="option.icon"
          :aria-pressed="sort === option.value"
          :title="option.title"
          @click="emit('update:sort', option.value)"
        >
          {{ option.label }}
        </UButton>
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
