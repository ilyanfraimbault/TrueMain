<script setup lang="ts">
import type { ChampionComparisonSide } from '~~/shared/types/champions'
import { formatCount } from '~~/shared/utils/counts'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { formatPointDelta, formatSigned } from '~~/shared/utils/deltas'
import { formatKdaRatio, KDA_DIGITS } from '~~/shared/utils/kda'

// The account-vs-mains comparison's figures: one column per side, the sample
// behind each in its header, and the account's delta under its value.
const props = defineProps<{
  player: ChampionComparisonSide
  mains: ChampionComparisonSide
  playerLabel: string
  mainsLabel: string
}>()

/**
 * The four side-by-side metrics (#528 scope). Build / skill order and
 * early-game leads are a separate issue and deliberately absent.
 *
 * `unit: 'points'` marks a rate whose delta reads in percentage points rather
 * than as a percentage of a percentage.
 */
interface MetricRow {
  label: string
  format: (value: number) => string
  formatDelta: (value: number) => string
  read: (side: ChampionComparisonSide) => number
}

const METRICS: MetricRow[] = [
  {
    label: 'Win rate',
    read: side => side.winRate,
    format: value => formatPercentage(value, 1),
    formatDelta: value => formatPointDelta(value),
  },
  {
    label: 'KDA',
    read: side => side.kda,
    format: formatKdaRatio,
    formatDelta: value => formatSigned(value, KDA_DIGITS),
  },
  {
    label: 'CS / min',
    read: side => side.csPerMin,
    format: value => value.toFixed(1),
    formatDelta: value => formatSigned(value, 1),
  },
  {
    label: 'Gold / min',
    read: side => side.goldPerMin,
    format: value => formatCount(Math.round(value)),
    formatDelta: value => formatSigned(value),
  },
]

// Higher is better for all four metrics, so one tone rule covers them: ahead of
// the mains reads as success, behind as error, a dead heat stays neutral. The
// epsilon keeps a rounding-level difference from being coloured as a real gap.
function deltaTone(delta: number, epsilon: number) {
  if (Math.abs(delta) < epsilon) return 'text-muted'
  return delta > 0 ? 'text-data-good' : 'text-data-bad'
}

const metricRows = computed(() => METRICS.map((metric) => {
  const playerValue = metric.read(props.player)
  const mainsValue = metric.read(props.mains)
  const delta = playerValue - mainsValue
  return {
    label: metric.label,
    playerValue: metric.format(playerValue),
    mainsValue: metric.format(mainsValue),
    delta: metric.formatDelta(delta),
    // Half of the smallest rendered step, so "+0.0" is never coloured.
    tone: deltaTone(delta, metric.label === 'Win rate' ? 0.0005 : 0.005),
  }
}))
</script>

<template>
  <div class="flex flex-col gap-2">
    <!-- Column headers: who each column is, and the sample behind it. -->
    <div class="grid grid-cols-[minmax(0,1fr)_auto_auto] items-end gap-x-3 gap-y-1">
      <span class="sr-only">Metric</span>
      <div class="flex flex-col items-end">
        <span class="max-w-[9rem] truncate text-xs font-medium text-default">
          {{ playerLabel }}
        </span>
        <span class="text-xs text-dimmed">{{ player.games }} games</span>
      </div>
      <div class="flex flex-col items-end">
        <span class="max-w-[9rem] truncate text-xs font-medium text-default">
          {{ mainsLabel }}
        </span>
        <span class="text-xs text-dimmed">{{ mains.games }} games</span>
      </div>

      <template
        v-for="row in metricRows"
        :key="row.label"
      >
        <span class="surface-hover -mx-1 rounded-md px-1 py-1 text-sm text-muted">
          {{ row.label }}
        </span>
        <div class="flex flex-col items-end py-1">
          <span
            class="text-sm font-semibold tabular-nums"
            :class="row.tone"
          >{{ row.playerValue }}</span>
          <span
            class="text-xs tabular-nums"
            :class="row.tone"
          >{{ row.delta }}</span>
        </div>
        <span class="py-1 text-right text-sm tabular-nums text-default">
          {{ row.mainsValue }}
        </span>
      </template>
    </div>
  </div>
</template>
