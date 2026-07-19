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

// Bars are drawn by hand (not <ChartsBarChart>) so each bar shares the exact same
// grid column as its icon below — the unovis band scale insets the first/last bar
// from the plot edges, which the full-width icon grid does not, leaving them
// visibly misaligned. Heights are relative to the strongest spike; a small floor
// keeps the weakest bar visible.
const MIN_BAR_PERCENT = 8

const maxSpike = computed(() =>
  bars.value.reduce((max, bar) => Math.max(max, bar.spikeMagnitude), 0),
)

const barHeightPercent = (spike: number): number =>
  maxSpike.value <= 0
    ? MIN_BAR_PERCENT
    : MIN_BAR_PERCENT + (100 - MIN_BAR_PERCENT) * (spike / maxSpike.value)

const formatGameTime = (minutes: number): string => formatDuration(Math.round(minutes * 60))
const formatGames = (count: number): string => count.toLocaleString('en-US')
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
        <!-- Bar + icon + completion time share one grid column each, so the bar
             sits exactly above its icon. -->
        <div
          class="grid items-end gap-2"
          :style="{ gridTemplateColumns: `repeat(${bars.length}, minmax(0, 1fr))` }"
        >
          <UTooltip
            v-for="bar in bars"
            :key="bar.item.id"
            :text="`${bar.item.name} · +${bar.spikeMagnitude.toFixed(2)} lead acceleration · completed ~${formatGameTime(bar.avgMinute)} · ${formatGames(bar.games)} games`"
          >
            <div class="flex flex-col items-center gap-1">
              <div class="flex h-28 w-full items-end justify-center">
                <div
                  class="w-6 rounded-t bg-primary transition-[height]"
                  :style="{ height: `${barHeightPercent(bar.spikeMagnitude)}%` }"
                />
              </div>
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
          </UTooltip>
        </div>
      </div>
    </div>
  </SectionCard>
</template>
