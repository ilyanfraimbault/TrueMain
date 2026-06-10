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
  Math.max(260, topTables.value.length * 28),
)
const sizeCategories = { bytes: { name: 'Total size', color: '#34d399' } }
// Bottom (value) axis — humanized bytes. Also used by the tooltip value.
const sizeValueFormatter = (tick: number | Date) => humanizeBytes(Number(tick), 0)
// Left (category) axis — table name looked up by bar index.
const sizeLabelFormatter = computed(() =>
  indexLabelFormatter(topTables.value, t => t.label),
)
// Tooltip title = the full table name for the hovered bar.
const sizeTooltipTitle = (d: { label: string }) => d.label
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
      <UAlert
        v-if="error"
        color="error"
        variant="subtle"
        icon="i-lucide-triangle-alert"
        title="Failed to load table sizes"
        :description="error.message"
        class="mb-6"
      />

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
            :tooltip-title-formatter="sizeTooltipTitle"
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
          sticky
          class="max-h-[640px]"
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
