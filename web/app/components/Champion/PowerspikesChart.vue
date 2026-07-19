<script setup lang="ts">
import type { ChampionPowerCurvePoint, ChampionPowerspikeEvent } from '~~/shared/types/champions'
import type { StaticItemData } from '~~/shared/types/static-data'
import { formatDuration } from '~/utils/relativeTime'

const props = withDefaults(defineProps<{
  curve: ChampionPowerCurvePoint[]
  events: ChampionPowerspikeEvent[]
  itemsMap: Record<number, StaticItemData>
  loading?: boolean
}>(), {
  loading: false,
})

// The hero is the opponent-relative power curve: 0 = even with the role
// opponent, positive = ahead. Unitless (σ-normalized blend of gold and damage
// lead), so the shape carries the meaning, not the absolute value.
interface CurveRow extends Record<string, unknown> {
  minute: number
  power: number
  games: number
}

const curveRows = computed<CurveRow[]>(() =>
  props.curve.map(point => ({
    minute: point.minute,
    power: point.power,
    games: point.games,
  })),
)

const hasCurve = computed(() => curveRows.value.length > 0)

const PRIMARY = defaultSeriesColor(0) // app primary (rosegold-400)
const curveCategories = { power: { name: 'Power', color: PRIMARY } }
const curveGradient = [
  { offset: '0%', stopOpacity: 0.35 },
  { offset: '100%', stopOpacity: 0.03 },
]

const xFormatter = (minute: number): string => `${minute}m`
const yFormatter = (value: number): string => {
  const sign = value > 0 ? '+' : ''
  return `${sign}${value.toFixed(1)}`
}

// One bar per completed build item that actually makes the champion stronger:
// positive spikes only, strongest first when capping, then re-ordered by the
// average completion time so the chart reads as a build timeline. The spike is
// baseline-subtracted (excess over the curve's ambient curvature), so a positive
// value now means "accelerates the lead more than usual at that minute".
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

const hasBars = computed(() => bars.value.length > 0)

// Chart rows for <ChartsBarChart>. The magnitude is a unitless slope change,
// so the y-axis is hidden — bar heights carry the relative read and the
// tooltip carries the exact numbers.
interface BarRow extends Record<string, unknown> {
  spike: number
  name: string
  minute: number
  games: number
}

const barRows = computed<BarRow[]>(() =>
  bars.value.map(bar => ({
    spike: bar.spikeMagnitude,
    name: bar.item.name,
    minute: bar.avgMinute,
    games: bar.games,
  })),
)

const barCategories = { spike: { name: 'Power spike' } }

const formatGameTime = (minutes: number): string => formatDuration(Math.round(minutes * 60))
</script>

<template>
  <SectionCard
    :level="2"
    title="Power spikes"
    subtitle="The champion's power relative to its role opponent over the game (positive = ahead), and which core items accelerate that lead the most once completed."
  >
    <USkeleton
      v-if="loading"
      class="h-64 w-full rounded-lg"
    />

    <p
      v-else-if="!hasCurve"
      class="py-8 text-center text-sm text-muted"
    >
      Not enough games yet to chart this champion's power curve in this role.
    </p>

    <div
      v-else
      class="space-y-4"
    >
      <ChartsAreaChart
        :data="curveRows"
        :categories="curveCategories"
        :height="220"
        :x-formatter="xFormatter"
        :y-formatter="yFormatter"
        :gradient-stops="curveGradient"
        hide-legend
      >
        <template #tooltip="{ values }">
          <div
            v-if="values"
            class="rounded-md border border-default bg-elevated px-2 py-1.5 text-xs shadow-md"
          >
            <p class="font-semibold text-default">
              {{ values.minute }} min
            </p>
            <p class="mt-0.5 tabular-nums text-muted">
              {{ yFormatter(values.power) }} power vs opponent
            </p>
            <p class="mt-0.5 text-muted">
              {{ values.games.toLocaleString('en-US') }} games
            </p>
          </div>
        </template>
      </ChartsAreaChart>

      <div v-if="hasBars">
        <p class="mb-1 text-xs font-medium text-muted">
          Item power spikes
        </p>
        <ChartsBarChart
          :data="barRows"
          :categories="barCategories"
          :y-axis="['spike']"
          :height="140"
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
    </div>
  </SectionCard>
</template>
