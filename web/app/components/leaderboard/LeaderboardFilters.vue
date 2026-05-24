<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { RegionSlug } from '~~/shared/types/leaderboard'
import type { ChampionPosition } from '~/utils/positions'

// Three independent filters wired through v-model. Region pills sit on the
// left (most discoverable filter), role + champion picker on the right —
// matches the screenshot's filter strip.
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

const REGION_OPTIONS: { value: RegionSlug, label: string }[] = [
  { value: 'europe', label: 'Europe' },
  { value: 'americas', label: 'Americas' },
  { value: 'korea', label: 'Korea' },
]

function toggleRegion(value: RegionSlug) {
  if (props.region === value) {
    emit('update:region', null)
  }
  else {
    emit('update:region', value)
  }
}

function clearAll() {
  emit('clear')
}

const hasAnyFilter = computed(() =>
  props.region !== null || props.position !== null || props.championId !== null)
</script>

<template>
  <div class="flex flex-wrap items-center gap-3">
    <div class="flex items-center gap-1 rounded-lg bg-elevated/40 p-1">
      <button
        v-for="option in REGION_OPTIONS"
        :key="option.value"
        type="button"
        class="inline-flex items-center gap-1.5 rounded px-2 py-1 text-xs font-medium transition-colors"
        :class="region === option.value
          ? 'bg-primary/25 text-default ring-1 ring-primary/50'
          : 'text-muted hover:bg-elevated/80 hover:text-default'"
        :aria-pressed="region === option.value"
        @click="toggleRegion(option.value)"
      >
        <LeaderboardRegionFlag :region="option.value" :width="18" />
        {{ option.label }}
      </button>
    </div>

    <RolePicker
      :position="position"
      @update:position="value => emit('update:position', value)"
    />

    <ChampionPicker
      :champions="champions"
      :champion-id="championId"
      @update:champion-id="value => emit('update:championId', value)"
    />

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
