<script setup lang="ts">
// Database panel — table sizes / row estimates from `GET /api/ops/db/tables`
// (returned total-bytes desc). Sortable table with humanized sizes plus a bar
// chart of the largest tables by total size.
import type { TableColumn } from '@nuxt/ui'
import type { DbTableRow } from '~~/shared/types/ops'
import { formatNumber, humanizeBytes } from '~~/shared/utils/format'

const { data, pending, error, refresh } = useDbTables()

// Client-side name filter — the dataset is small (one row per table) so
// filtering in the browser is fine and avoids a round-trip.
const search = ref('')
const rows = computed<DbTableRow[]>(() => {
  const all = data.value ?? []
  const term = search.value.trim().toLowerCase()
  if (!term) {
    return all
  }
  return all.filter(t => t.tableName.toLowerCase().includes(term))
})

const totalDbBytes = computed(() =>
  (data.value ?? []).reduce((sum, t) => sum + (t.totalBytes ?? 0), 0),
)

// --- Table -------------------------------------------------------------------
const sorting = ref([{ id: 'totalBytes', desc: true }])

const columns: TableColumn<DbTableRow>[] = [
  {
    accessorKey: 'tableName',
    header: ({ column }) => sortableHeader(column, 'Table'),
  },
  {
    accessorKey: 'rowEstimate',
    header: ({ column }) => sortableHeader(column, 'Rows (est.)', 'right'),
  },
  {
    accessorKey: 'totalBytes',
    header: ({ column }) => sortableHeader(column, 'Total size', 'right'),
  },
  {
    accessorKey: 'tableBytes',
    header: ({ column }) => sortableHeader(column, 'Table size', 'right'),
  },
  {
    accessorKey: 'indexBytes',
    header: ({ column }) => sortableHeader(column, 'Index size', 'right'),
  },
]

// --- Chart: top tables by total size -----------------------------------------
// Rendered HORIZONTALLY via `horizontalBarProps()`: table names are long
// snake_case strings that collide badly on a vertical x-axis, so the category
// axis goes on the LEFT where full names fit. In vue-chrts the bar `x` accessor
// is always the data index and `y` the value; with horizontal orientation
// unovis maps the value to the bottom (x) axis and the data index to the left
// (y) axis — so the formatters are intentionally "swapped" relative to a
// vertical chart: `xFormatter` formats the byte VALUE, `yFormatter` looks up the
// table-name LABEL by index. (Verified against vue-chrts@2.1.4 BarChart.js /
// @unovis/ts stacked-bar dataScale/valueScale.)
const TOP_N = 12
const topTables = computed(() =>
  [...(data.value ?? [])]
    .sort((a, b) => b.totalBytes - a.totalBytes)
    .slice(0, TOP_N)
    .map(t => ({ label: t.tableName, bytes: t.totalBytes })),
)
// Chart grows with the number of bars; the skeleton mirrors it to avoid CLS.
const topTablesChartHeight = computed(() =>
  barChartHeight(topTables.value.length, { min: 260, step: 28 }),
)
const sizeCategories = { bytes: { name: 'Total size', color: CHART_PRIMARY } }
// Bottom (value) axis — humanized bytes. Also used by the tooltip value.
const sizeValueFormatter = (tick: number | Date) => humanizeBytes(Number(tick), 0)
// Left (category) axis — table name looked up by bar index.
const sizeLabelFormatter = computed(() =>
  indexLabelFormatter(topTables.value, t => t.label),
)

// --- Growth history + disk forecast (#925) -----------------------------------
// Everything below reads the daily snapshot collection, never a live pg_catalog
// scan, so widening the window costs nothing on the database.
const WINDOW_OPTIONS = [
  { label: '30 days', value: 30 },
  { label: '90 days', value: 90 },
  { label: '1 year', value: 365 },
]
const windowDays = ref(90)
const { data: history, pending: historyPending, error: historyError } = useDbStorageHistory(windowDays)

const dailyPoints = computed(() => history.value?.daily ?? [])
// Two points is the minimum that draws a line; one renders as a dot and reads
// as a bug. The backend needs three before it will forecast — the chart is
// deliberately less strict, since showing two real points is not a projection.
const hasGrowthTrend = computed(() => dailyPoints.value.length > 1)

const growthRows = computed(() =>
  dailyPoints.value.map(point => ({
    label: new Date(point.dateUtc).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }),
    databaseBytes: point.databaseBytes,
  })),
)
const growthCategories = { databaseBytes: { name: 'Database size', color: CHART_PRIMARY } }
const growthValueFormatter = (tick: number | Date) => humanizeBytes(Number(tick), 1)
const growthLabelFormatter = computed(() =>
  indexLabelFormatter(growthRows.value, row => row.label),
)

// Rows created per day, derived from consecutive snapshots. The first day has no
// predecessor, so the series is one point shorter than the size series.
const rowsPerDayRows = computed(() =>
  dailyPoints.value.slice(1).map((point, index) => ({
    label: new Date(point.dateUtc).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }),
    rows: Math.max(0, point.rowEstimate - dailyPoints.value[index]!.rowEstimate),
  })),
)
const hasRowsPerDay = computed(() => rowsPerDayRows.value.length > 1)
const rowsPerDayCategories = { rows: { name: 'Rows added', color: CHART_PRIMARY } }
const rowsPerDayValueFormatter = (tick: number | Date) => formatCount(Number(tick))
const rowsPerDayLabelFormatter = computed(() =>
  indexLabelFormatter(rowsPerDayRows.value, row => row.label),
)

const forecast = computed(() => history.value?.forecast ?? null)

// Why there is no forecast, in the operator's terms. The backend deliberately
// returns null rather than a placeholder date, so the panel has to say which of
// the three reasons applies instead of rendering an empty card.
const forecastAbsenceReason = computed(() => {
  if (historyPending.value || forecast.value) {
    return null
  }
  if (dailyPoints.value.length === 0) {
    return 'No snapshots recorded yet — the ingestor writes one per pipeline run.'
  }
  if (dailyPoints.value.length < 3) {
    return `Only ${dailyPoints.value.length} day(s) of history — three are needed before a trend can be fitted.`
  }
  return 'Storage is flat or shrinking over this window, or no disk capacity is configured (StorageHistory:DiskCapacityBytes).'
})

function crossingLabel(projectedAtUtc: string | null): string {
  if (projectedAtUtc === null) {
    return 'Not foreseeable at this rate'
  }
  const date = new Date(projectedAtUtc)
  const label = date.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
  return date.getTime() < Date.now() ? `Already exceeded (${label})` : label
}

function crossingColor(projectedAtUtc: string | null): 'error' | 'warning' | 'neutral' {
  if (projectedAtUtc === null) {
    return 'neutral'
  }
  const days = (new Date(projectedAtUtc).getTime() - Date.now()) / 86_400_000
  // A month is roughly the lead time needed to resize a volume without drama.
  return days < 0 ? 'error' : days < 30 ? 'warning' : 'neutral'
}
</script>

<template>
  <UDashboardPanel id="database">
    <template #header>
      <UDashboardNavbar title="Database" icon="i-lucide-database">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            icon="i-lucide-refresh-cw"
            color="neutral"
            variant="ghost"
            :loading="pending"
            aria-label="Refresh"
            @click="refresh()"
          />
        </template>
      </UDashboardNavbar>

      <UDashboardToolbar>
        <template #left>
          <UInput
            v-model="search"
            icon="i-lucide-search"
            placeholder="Filter tables…"
            class="w-64"
          />
        </template>
        <template #right>
          <UBadge
            v-if="!pending"
            color="neutral"
            variant="subtle"
            :label="`${humanizeBytes(totalDbBytes)} total`"
          />
        </template>
      </UDashboardToolbar>
    </template>

    <template #body>
      <FetchErrorAlert
        v-if="error"
        :error="error"
        title="Failed to load table sizes"
        class="mb-6"
      />

      <FetchErrorAlert
        v-if="historyError"
        :error="historyError"
        title="Failed to load storage history"
        class="mb-6"
      />

      <!-- Disk forecast (#925): the reason this panel keeps history at all. -->
      <UCard class="mb-6">
        <template #header>
          <div class="flex items-center justify-between gap-2">
            <p class="text-xs text-muted uppercase">
              Disk forecast
            </p>
            <UBadge
              v-if="forecast"
              color="neutral"
              variant="subtle"
              :label="`+${humanizeBytes(forecast.bytesPerDay, 1)}/day`"
            />
          </div>
        </template>

        <USkeleton v-if="historyPending" class="h-16 w-full" />
        <p v-else-if="forecastAbsenceReason" class="text-sm text-muted">
          {{ forecastAbsenceReason }}
        </p>
        <div v-else-if="forecast" class="flex flex-wrap gap-3">
          <div
            v-for="crossing in forecast.crossings"
            :key="crossing.percent"
            class="flex min-w-[12rem] flex-col gap-1 rounded-lg border border-default px-3 py-2"
          >
            <p class="text-xs text-muted">
              {{ crossing.percent }}% of {{ humanizeBytes(forecast.diskCapacityBytes, 0) }}
              ({{ humanizeBytes(crossing.thresholdBytes, 0) }})
            </p>
            <UBadge
              :color="crossingColor(crossing.projectedAtUtc)"
              variant="subtle"
              :label="crossingLabel(crossing.projectedAtUtc)"
            />
          </div>
        </div>
      </UCard>

      <!-- Growth over time -->
      <UCard class="mb-6" :ui="{ root: 'overflow-visible' }">
        <template #header>
          <div class="flex items-center justify-between gap-2">
            <p class="text-xs text-muted uppercase">
              Database size over time
            </p>
            <USelect
              v-model="windowDays"
              :items="WINDOW_OPTIONS"
              value-key="value"
              size="xs"
              class="w-32"
            />
          </div>
        </template>
        <USkeleton v-if="historyPending" class="h-[260px] w-full" />
        <div
          v-else-if="!hasGrowthTrend"
          class="flex h-[260px] items-center justify-center text-center text-sm text-muted"
        >
          Not enough snapshots yet to draw a trend — one point is recorded per day.
        </div>
        <ClientOnly v-else>
          <NcAreaChart
            :data="growthRows"
            :height="260"
            :categories="growthCategories"
            :x-num-ticks="Math.min(growthRows.length, 8)"
            :x-formatter="growthLabelFormatter"
            :y-formatter="growthValueFormatter"
            v-bind="areaChartProps()"
          />
          <template #fallback>
            <USkeleton class="h-[260px] w-full" />
          </template>
        </ClientOnly>
      </UCard>

      <!-- Rows added per day -->
      <UCard class="mb-6" :ui="{ root: 'overflow-visible' }">
        <template #header>
          <p class="text-xs text-muted uppercase">
            Rows added per day (estimated)
          </p>
        </template>
        <USkeleton v-if="historyPending" class="h-[220px] w-full" />
        <div
          v-else-if="!hasRowsPerDay"
          class="flex h-[220px] items-center justify-center text-center text-sm text-muted"
        >
          Needs at least three days of snapshots — each point is the difference between two days.
        </div>
        <ClientOnly v-else>
          <NcAreaChart
            :data="rowsPerDayRows"
            :height="220"
            :categories="rowsPerDayCategories"
            :x-num-ticks="Math.min(rowsPerDayRows.length, 8)"
            :x-formatter="rowsPerDayLabelFormatter"
            :y-formatter="rowsPerDayValueFormatter"
            v-bind="areaChartProps()"
          />
          <template #fallback>
            <USkeleton class="h-[220px] w-full" />
          </template>
        </ClientOnly>
      </UCard>

      <!-- Fastest-growing tables -->
      <UCard v-if="(history?.tables?.length ?? 0) > 0" class="mb-6" :ui="{ body: 'p-0 sm:p-0' }">
        <template #header>
          <p class="text-sm font-medium text-highlighted">
            Growth by table
          </p>
        </template>
        <div class="divide-y divide-default">
          <div
            v-for="series in history!.tables"
            :key="series.tableName"
            class="flex items-center justify-between gap-4 px-4 py-2"
          >
            <span class="font-mono text-sm text-highlighted truncate">{{ series.tableName }}</span>
            <div class="flex shrink-0 items-center gap-4 tabular-nums text-sm">
              <span class="text-muted">{{ humanizeBytes(series.currentBytes) }}</span>
              <span class="w-28 text-right" :class="series.bytesPerDay > 0 ? 'text-highlighted' : 'text-muted'">
                {{ series.bytesPerDay >= 0 ? '+' : '' }}{{ humanizeBytes(series.bytesPerDay, 1) }}/d
              </span>
              <span class="w-24 text-right text-muted">
                {{ series.rowsPerDay >= 0 ? '+' : '' }}{{ formatCount(series.rowsPerDay) }} rows/d
              </span>
              <span class="w-16 text-right text-muted">
                {{ series.growthRate === null ? '—' : `${(series.growthRate * 100).toFixed(0)}%` }}
              </span>
            </div>
          </div>
        </div>
      </UCard>

      <!-- Top tables by size -->
      <UCard class="mb-6" :ui="{ root: 'overflow-visible' }">
        <template #header>
          <p class="text-xs text-muted uppercase">
            Top {{ TOP_N }} tables by total size
          </p>
        </template>
        <USkeleton
          v-if="pending"
          class="w-full"
          :style="{ height: `${topTablesChartHeight}px` }"
        />
        <div
          v-else-if="topTables.length === 0"
          class="flex items-center justify-center text-sm text-muted"
          :style="{ height: `${topTablesChartHeight}px` }"
        >
          No tables reported.
        </div>
        <ClientOnly v-else>
          <NcBarChart
            :data="topTables"
            :height="topTablesChartHeight"
            :categories="sizeCategories"
            :y-axis="['bytes']"
            :x-formatter="sizeValueFormatter"
            :y-formatter="sizeLabelFormatter"
            :y-num-ticks="topTables.length"
            :tooltip-title-formatter="labelTooltipTitle"
            v-bind="horizontalBarProps(180)"
          />
          <template #fallback>
            <USkeleton
              class="w-full"
              :style="{ height: `${topTablesChartHeight}px` }"
            />
          </template>
        </ClientOnly>
      </UCard>

      <!-- Table list -->
      <UCard :ui="{ body: 'p-0 sm:p-0' }">
        <template #header>
          <div class="flex items-center justify-between gap-2">
            <p class="text-sm font-medium text-highlighted">
              Tables
            </p>
            <UBadge
              v-if="!pending"
              color="neutral"
              variant="subtle"
              :label="`${formatNumber(rows.length)} tables`"
            />
          </div>
        </template>

        <UTable
          v-model:sorting="sorting"
          :data="rows"
          :columns="columns"
          :loading="pending"
          loading-color="primary"
          :ui="{ td: 'py-2' }"
        >
          <template #tableName-cell="{ row }">
            <span class="font-medium text-highlighted font-mono text-sm">
              {{ row.original.tableName }}
            </span>
          </template>
          <template #rowEstimate-cell="{ row }">
            <div class="text-right tabular-nums">
              {{ formatNumber(row.original.rowEstimate) }}
            </div>
          </template>
          <template #totalBytes-cell="{ row }">
            <div class="text-right tabular-nums font-medium text-highlighted">
              {{ humanizeBytes(row.original.totalBytes) }}
            </div>
          </template>
          <template #tableBytes-cell="{ row }">
            <div class="text-right tabular-nums text-muted">
              {{ humanizeBytes(row.original.tableBytes) }}
            </div>
          </template>
          <template #indexBytes-cell="{ row }">
            <div class="text-right tabular-nums text-muted">
              {{ humanizeBytes(row.original.indexBytes) }}
            </div>
          </template>

          <template #empty>
            <div class="py-10 text-center text-sm text-muted">
              No tables match this filter.
            </div>
          </template>
        </UTable>
      </UCard>
    </template>
  </UDashboardPanel>
</template>
