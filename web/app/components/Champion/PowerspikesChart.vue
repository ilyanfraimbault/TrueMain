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

// One bar per completed build item, in the order the build comes online. Level
// milestones (6/11/16) are also in the payload but out of scope for this bar
// view. The endpoint orders by *signed* magnitude, so re-rank by absolute
// magnitude before capping — a strong negative spike is as worth showing as a
// strong positive one.
const MAX_ITEMS = 8

interface SpikeBar {
  item: StaticItemData
  avgMinute: number
  spikeMagnitude: number
  games: number
}

const bars = computed<SpikeBar[]>(() =>
  props.events
    .filter(event => event.type === 'item')
    .map(event => ({
      item: props.itemsMap[event.refId],
      avgMinute: event.avgMinute,
      spikeMagnitude: event.spikeMagnitude,
      games: event.games,
    }))
    .filter((entry): entry is SpikeBar => entry.item !== undefined)
    .sort((left, right) => Math.abs(right.spikeMagnitude) - Math.abs(left.spikeMagnitude))
    .slice(0, MAX_ITEMS)
    .sort((left, right) => left.avgMinute - right.avgMinute),
)

const hasData = computed(() => bars.value.length > 0)

// Split the chart area between the positive (above baseline) and negative
// (below) regions proportionally to the data, so an all-positive read uses the
// full height instead of wasting the bottom half.
const maxPositive = computed(() =>
  bars.value.reduce((max, bar) => Math.max(max, bar.spikeMagnitude), 0),
)
const maxNegative = computed(() =>
  bars.value.reduce((max, bar) => Math.max(max, -bar.spikeMagnitude), 0),
)
const positiveRatio = computed(() => {
  const total = maxPositive.value + maxNegative.value
  return total > 0 ? maxPositive.value / total : 1
})

function barHeightPercent(bar: SpikeBar): number {
  const magnitude = Math.abs(bar.spikeMagnitude)
  const scale = bar.spikeMagnitude >= 0 ? maxPositive.value : maxNegative.value
  return scale > 0 ? (magnitude / scale) * 100 : 0
}

const formatGameTime = (minutes: number): string => formatDuration(Math.round(minutes * 60))

// Sign off the *rounded* value so a near-zero negative never reads "−0.00".
function formatMagnitude(value: number): string {
  const rounded = Number(value.toFixed(2))
  if (rounded === 0) return '0.00'
  return `${rounded > 0 ? '+' : '−'}${Math.abs(rounded).toFixed(2)}`
}

function barTooltip(bar: SpikeBar): string {
  return `${bar.item.name} · ${formatMagnitude(bar.spikeMagnitude)} spike at ${formatGameTime(bar.avgMinute)} · ${bar.games.toLocaleString('en-US')} games`
}
</script>

<template>
  <SectionCard
    :level="2"
    title="Power spikes"
    subtitle="How much the champion's lead accelerates once each core item is completed, at its average completion time."
  >
    <USkeleton
      v-if="loading"
      class="h-52 w-full rounded-lg"
    />

    <p
      v-else-if="!hasData"
      class="py-8 text-center text-sm text-muted"
    >
      No power-spike data yet for this champion and lane.
    </p>

    <div
      v-else
      class="flex items-stretch justify-start gap-3 sm:gap-4"
    >
      <div
        v-for="bar in bars"
        :key="bar.item.id"
        class="flex w-14 flex-col items-center gap-1.5"
      >
        <UTooltip
          :text="barTooltip(bar)"
          :delay-duration="150"
          class="w-full"
        >
          <div class="flex h-36 w-full flex-col">
            <!-- Positive region: bars grow up from the baseline. -->
            <div
              class="flex min-h-0 flex-col items-center justify-end"
              :style="{ flexGrow: positiveRatio }"
            >
              <div
                v-if="bar.spikeMagnitude >= 0"
                class="w-6 rounded-t bg-gradient-to-t from-emerald-600/70 to-emerald-400"
                :style="{ height: `${barHeightPercent(bar)}%` }"
              />
            </div>
            <div class="w-full border-t border-default/60" />
            <!-- Negative region: bars grow down from the baseline. -->
            <div
              class="flex min-h-0 flex-col items-center justify-start"
              :style="{ flexGrow: 1 - positiveRatio }"
            >
              <div
                v-if="bar.spikeMagnitude < 0"
                class="w-6 rounded-b bg-gradient-to-b from-amber-400 to-amber-600/70"
                :style="{ height: `${barHeightPercent(bar)}%` }"
              />
            </div>
          </div>
        </UTooltip>

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
  </SectionCard>
</template>
