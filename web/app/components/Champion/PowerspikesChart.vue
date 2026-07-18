<script setup lang="ts">
import type { ChampionPowerspikeEvent } from '~~/shared/types/champions'
import type { StaticItemData } from '~~/shared/types/static-data'
import { formatDuration } from '~/utils/relativeTime'

const props = withDefaults(defineProps<{
  events: ChampionPowerspikeEvent[]
  itemsMap: Record<number, StaticItemData>
  loading?: boolean
}>(), {
  loading: false,
})

// One bar per completed build item that actually makes the champion stronger:
// positive spikes only, strongest first when capping, then re-ordered by the
// average completion time so the chart reads as a build timeline. Negative
// spikes are noise for the reader (an item bought while already losing, low
// samples) and level milestones are out of scope for this view.
const MAX_ITEMS = 6

interface SpikeBar {
  item: StaticItemData
  avgMinute: number
  spikeMagnitude: number
  games: number
}

const bars = computed<SpikeBar[]>(() =>
  props.events
    .filter(event => event.type === 'item' && event.spikeMagnitude > 0)
    .map(event => ({
      item: props.itemsMap[event.refId],
      avgMinute: event.avgMinute,
      spikeMagnitude: event.spikeMagnitude,
      games: event.games,
    }))
    .filter((entry): entry is SpikeBar => entry.item !== undefined)
    .sort((left, right) => right.spikeMagnitude - left.spikeMagnitude)
    .slice(0, MAX_ITEMS)
    .sort((left, right) => left.avgMinute - right.avgMinute),
)

const hasData = computed(() => bars.value.length > 0)

// Chart rows for <ChartsBarChart>. The magnitude is a unitless slope change,
// so the y-axis is hidden — bar heights carry the relative read and the
// tooltip carries the exact numbers.
interface ChartRow extends Record<string, unknown> {
  spike: number
  name: string
  minute: number
  games: number
}

const rows = computed<ChartRow[]>(() =>
  bars.value.map(bar => ({
    spike: bar.spikeMagnitude,
    name: bar.item.name,
    minute: bar.avgMinute,
    games: bar.games,
  })),
)

const categories = { spike: { name: 'Power spike' } }

const formatGameTime = (minutes: number): string => formatDuration(Math.round(minutes * 60))
</script>

<template>
  <SectionCard
    :level="2"
    title="Power spikes"
    subtitle="How much each core item accelerates the champion's lead once completed. Only items with a clear positive spike are shown."
  >
    <USkeleton
      v-if="loading"
      class="h-52 w-full rounded-lg"
    />

    <p
      v-else-if="!hasData"
      class="py-8 text-center text-sm text-muted"
    >
      No item gives this champion a clear power spike yet in this lane.
    </p>

    <div v-else>
      <ChartsBarChart
        :data="rows"
        :categories="categories"
        :y-axis="['spike']"
        :height="160"
        :bar-padding="0.7"
        :y-grid-line="false"
        hide-legend
        hide-x-axis
        hide-y-axis
      >
        <template #tooltip="{ values }">
          <div
            v-if="values"
            class="rounded-md border border-default bg-elevated px-2 py-1.5 text-xs shadow-md"
          >
            <p class="font-semibold text-default">
              {{ values.name }}
            </p>
            <p class="mt-0.5 tabular-nums text-muted">
              +{{ values.spike.toFixed(2) }} lead acceleration · completed ~{{ formatGameTime(values.minute) }}
            </p>
            <p class="mt-0.5 text-muted">
              {{ values.games.toLocaleString('en-US') }} games
            </p>
          </div>
        </template>
      </ChartsBarChart>

      <!-- Icon + completion-time labels, one column per bar. The chart hides
           both axes, so its plot area spans the full width and the unovis
           band scale matches this equal-column grid. -->
      <div
        class="mt-2 grid"
        :style="{ gridTemplateColumns: `repeat(${bars.length}, minmax(0, 1fr))` }"
      >
        <div
          v-for="bar in bars"
          :key="bar.item.id"
          class="flex flex-col items-center gap-1"
        >
          <GameTooltipItemIcon
            :item="bar.item"
            :width="32"
            :height="32"
            class="size-8 rounded"
          />
          <span class="text-xs font-medium tabular-nums text-muted">
            {{ formatGameTime(bar.avgMinute) }}
          </span>
        </div>
      </div>
    </div>
  </SectionCard>
</template>
