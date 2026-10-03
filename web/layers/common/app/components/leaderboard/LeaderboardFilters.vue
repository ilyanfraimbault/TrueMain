<script setup lang="ts">
import type { RegionSlug } from '#shared/types/leaderboard'
import type { ChampionPosition } from '#common/utils/positions'

// Position anchors the left edge and region the right edge; the OTP toggle
// sits in a `flex-1 justify-center` middle group so it's centered in the gap
// between the two rather than crowding next to the position picker. The order
// is not a filter: the table's Rank and Score headers set it (#1734). Champion
// filtering lives in the page's top AppSearch bar, not here. Each filter has
// its own reset affordance: the position picker's "All" button and the
// region select's "All regions" entry. There's no global Clear button —
// per-field clearing is faster and avoids the nuclear option for a single
// mis-click.
const props = defineProps<{
  region: RegionSlug | null
  position: ChampionPosition | null
  otpOnly: boolean
}>()

const emit = defineEmits<{
  'update:region': [value: RegionSlug | null]
  'update:position': [value: ChampionPosition | null]
  'update:otpOnly': [value: boolean]
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
    <!-- Position: narrowest filter, anchors the left edge. Reuses the same
         component the /champions page uses so the two filter strips feel
         identical. -->
    <RolePicker
      :position="position"
      @update:position="value => emit('update:position', value)"
    />

    <!-- OTP centered in the gap between position and region. -->
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
    </div>

    <!-- Region: rightmost, single dropdown so the strip stays compact and
         each region is reachable in one click. The flag renders in both
         the trigger and the option rows via USelectMenu slots. -->
    <USelectMenu
      :model-value="selectedRegion"
      :items="REGION_OPTIONS"
      :search-input="false"
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
