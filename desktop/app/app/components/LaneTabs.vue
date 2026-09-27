<script setup lang="ts">
import type { Lane } from '~/types/draft'
import { LANES, LANE_LABELS, laneIconUrl } from '~/types/draft'

/**
 * A lane filter as a row of Riot's lane glyphs — "All" first when the page can
 * show every lane at once. `null` is all lanes.
 */
const props = withDefaults(defineProps<{
  /** Offer "All" before the five lanes. */
  all?: boolean
  /** Only these lanes; the rest are hidden (a champion's page offers the lanes it is played on). */
  lanes?: readonly Lane[]
  labels?: boolean
}>(), { all: false, lanes: () => LANES, labels: false })

const model = defineModel<Lane | null>({ default: null })

const options = computed(() => LANES.filter(lane => props.lanes.includes(lane)))
</script>

<template>
  <div class="inline-flex items-center gap-0.5 rounded-lg border border-default bg-elevated p-0.5">
    <button
      v-if="all"
      type="button"
      class="h-7 rounded-md px-2.5 text-xs font-medium transition-colors"
      :class="model === null ? 'bg-accented text-highlighted' : 'text-muted hover:text-default'"
      @click="model = null"
    >
      All
    </button>
    <button
      v-for="lane in options"
      :key="lane"
      type="button"
      class="flex h-7 items-center gap-1.5 rounded-md px-2 text-xs font-medium transition-colors"
      :class="model === lane ? 'bg-accented text-highlighted' : 'text-muted hover:text-default'"
      :title="LANE_LABELS[lane]"
      :aria-label="LANE_LABELS[lane]"
      :aria-pressed="model === lane"
      @click="model = lane"
    >
      <img :src="laneIconUrl(lane)" alt="" class="size-4" :class="model !== lane && 'opacity-60'">
      <span v-if="labels">{{ LANE_LABELS[lane] }}</span>
    </button>
  </div>
</template>
