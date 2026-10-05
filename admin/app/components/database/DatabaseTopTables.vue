<script setup lang="ts">
// Database panel: the largest tables by total size, from the page's
// `GET /api/ops/db/tables` payload (both engines, #1023).
import type { DbTableRow } from '~~/shared/types/ops'
import { humanizeBytes } from '~~/shared/utils/format'

const props = defineProps<{
  tables: DbTableRow[] | null | undefined
  pending: boolean
}>()

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
  [...(props.tables ?? [])]
    .sort((a, b) => b.totalBytes - a.totalBytes)
    .slice(0, TOP_N)
    // The label carries the engine: two of these names exist on both sides, and a
    // bar chart has no other column to tell them apart.
    .map(t => ({ label: `${t.tableName} (${storageEngineLabel(t.engine)})`, bytes: t.totalBytes })),
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
</script>

<template>
  <UCard class="mb-6" :ui="{ root: 'overflow-visible' }">
    <template #header>
      <PanelTitle variant="label" :title="`Top ${TOP_N} tables by total size`" />
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
    <ChartsBarChart
      v-else
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
  </UCard>
</template>
