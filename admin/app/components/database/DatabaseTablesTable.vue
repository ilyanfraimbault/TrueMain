<script setup lang="ts">
// Database panel: the sortable list of Postgres tables and Mongo collections with
// humanized sizes. The page filters the rows by name before handing them over.
import type { TableColumn } from '@nuxt/ui'
import type { DbTableRow } from '~~/shared/types/ops'
import { formatNumber, humanizeBytes } from '~~/shared/utils/format'

defineProps<{
  rows: DbTableRow[]
  pending: boolean
}>()

const sorting = ref([{ id: 'totalBytes', desc: true }])

const columns: TableColumn<DbTableRow>[] = [
  {
    accessorKey: 'engine',
    header: ({ column }) => sortableHeader(column, 'Engine'),
  },
  {
    accessorKey: 'tableName',
    header: ({ column }) => sortableHeader(column, 'Table / collection'),
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
</script>

<template>
  <UCard :ui="{ body: 'p-0 sm:p-0' }">
    <template #header>
      <div class="flex items-center justify-between gap-2">
        <PanelTitle title="Tables" />
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
      <template #engine-cell="{ row }">
        <UBadge
          variant="subtle"
          :color="row.original.engine === 'mongo' ? 'success' : 'info'"
          size="sm"
        >
          {{ storageEngineLabel(row.original.engine) }}
        </UBadge>
      </template>
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
