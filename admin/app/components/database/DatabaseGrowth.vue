<script setup lang="ts">
// Database panel: growth history (#925) — disk size over time, rows added per day
// and the fastest-growing tables. Everything here reads the daily snapshot
// collection (`GET /api/ops/db/history`, fetched by the page), never a live
// pg_catalog scan, so widening the window costs nothing on the database.
import type { DbStorageHistory } from '~~/shared/types/ops'
import { formatDayLabel, formatPercentOrDash, humanizeBytes } from '~~/shared/utils/format'

const props = defineProps<{
  history: DbStorageHistory | null | undefined
  pending: boolean
}>()

const windowDays = defineModel<number>('windowDays', { required: true })

const WINDOW_OPTIONS = [
  { label: '30 days', value: 30 },
  { label: '90 days', value: 90 },
  { label: '1 year', value: 365 },
]

const dailyPoints = computed(() => props.history?.daily ?? [])
// Two points is the minimum that draws a line; one renders as a dot and reads
// as a bug. The backend needs three before it will forecast — the chart is
// deliberately less strict, since showing two real points is not a projection.
const hasGrowthTrend = computed(() => dailyPoints.value.length > 1)

const growthRows = computed(() =>
  dailyPoints.value.map(point => ({
    label: formatDayLabel(point.dateUtc),
    databaseBytes: point.databaseBytes,
  })),
)
// One series, the summed on-disk size — the same number the forecast projects.
// Stays an AREA: this is a stock, a level the volume actually sits at, which is
// exactly what a filled line is for (#1218).
// The per-engine split is stated in the forecast card rather than drawn as two
// stacked series: the operator's question here is "is the volume filling up",
// and that is one line.
const growthCategories = { databaseBytes: { name: 'Disk size (Postgres + Mongo)', color: CHART_PRIMARY } }
const growthValueFormatter = (tick: number | Date) => humanizeBytes(Number(tick), 1)
const growthLabelFormatter = computed(() =>
  indexLabelFormatter(growthRows.value, row => row.label),
)

// Rows created per day, derived from consecutive snapshots. The first day has no
// predecessor, so the series is one point shorter than the size series.
// BARS, unlike the disk-size chart above (#1218): this series is the day's delta,
// a flow, while disk size is the level itself — the one place on this page where
// the two forms sit next to each other and the difference is visible.
const rowsPerDayRows = computed(() =>
  dailyPoints.value.slice(1).map((point, index) => ({
    label: formatDayLabel(point.dateUtc),
    rows: Math.max(0, point.rowEstimate - dailyPoints.value[index]!.rowEstimate),
  })),
)
const hasRowsPerDay = computed(() => rowsPerDayRows.value.length > 1)
const rowsPerDayCategories = { rows: { name: 'Rows added', color: CHART_PRIMARY } }
const rowsPerDayValueFormatter = (tick: number | Date) => formatCount(Number(tick))
const rowsPerDayLabelFormatter = computed(() =>
  indexLabelFormatter(rowsPerDayRows.value, row => row.label),
)
</script>

<template>
  <!-- Growth over time -->
  <UCard class="mb-6" :ui="{ root: 'overflow-visible' }">
    <template #header>
      <div class="flex items-center justify-between gap-2">
        <PanelTitle variant="label" title="Database size over time" />
        <USelect
          v-model="windowDays"
          :items="WINDOW_OPTIONS"
          value-key="value"
          size="xs"
          class="w-32"
        />
      </div>
    </template>
    <ChartsAreaChart
      :data="hasGrowthTrend ? growthRows : []"
      :height="260"
      :categories="growthCategories"
      :loading="pending"
      empty-message="Not enough snapshots yet to draw a trend — one point is recorded per day."
      :x-num-ticks="Math.min(growthRows.length, 8)"
      :x-formatter="growthLabelFormatter"
      :y-formatter="growthValueFormatter"
    />
  </UCard>

  <!-- Rows added per day -->
  <UCard class="mb-6" :ui="{ root: 'overflow-visible' }">
    <template #header>
      <PanelTitle
        variant="label"
        title="Rows added per day (estimated)"
        info="Each point is the difference between two daily snapshots, so the
          series needs at least three days before it can be drawn."
      />
    </template>
    <USkeleton v-if="pending" class="h-[220px] w-full" />
    <div
      v-else-if="!hasRowsPerDay"
      class="flex h-[220px] items-center justify-center text-center text-sm text-muted"
    >
      Needs at least three days of snapshots — each point is the difference between two days.
    </div>
    <ChartsBarChart
      v-else
      :data="rowsPerDayRows"
      :height="220"
      :categories="rowsPerDayCategories"
      :y-axis="['rows']"
      :x-num-ticks="Math.min(rowsPerDayRows.length, 8)"
      :x-formatter="rowsPerDayLabelFormatter"
      :y-formatter="rowsPerDayValueFormatter"
      :tooltip-title-formatter="labelTooltipTitle"
      v-bind="timeBarProps()"
    />
  </UCard>

  <!-- Fastest-growing tables -->
  <UCard v-if="(history?.tables?.length ?? 0) > 0" class="mb-6" :ui="{ body: 'p-0 sm:p-0' }">
    <template #header>
      <PanelTitle title="Growth by table" />
    </template>
    <div class="divide-y divide-default">
      <div
        v-for="series in history!.tables"
        :key="`${series.engine}:${series.tableName}`"
        class="flex items-center justify-between gap-4 px-4 py-2"
      >
        <span class="flex min-w-0 items-center gap-2">
          <UBadge
            variant="subtle"
            :color="series.engine === 'mongo' ? 'success' : 'info'"
            size="sm"
          >
            {{ storageEngineLabel(series.engine) }}
          </UBadge>
          <span class="font-mono text-sm text-highlighted truncate">{{ series.tableName }}</span>
        </span>
        <div class="flex shrink-0 items-center gap-4 tabular-nums text-sm">
          <span class="text-muted">{{ humanizeBytes(series.currentBytes) }}</span>
          <span class="w-28 text-right" :class="series.bytesPerDay > 0 ? 'text-highlighted' : 'text-muted'">
            {{ series.bytesPerDay >= 0 ? '+' : '' }}{{ humanizeBytes(series.bytesPerDay, 1) }}/d
          </span>
          <span class="w-24 text-right text-muted">
            {{ series.rowsPerDay >= 0 ? '+' : '' }}{{ formatCount(series.rowsPerDay) }} rows/d
          </span>
          <span class="w-16 text-right text-muted">
            {{ formatPercentOrDash(series.growthRate, 0) }}
          </span>
        </div>
      </div>
    </div>
  </UCard>
</template>
