<script setup lang="ts">
// Runs table of the Processes page: one server page of `GET /api/ops/process-runs`
// with its pager. Failed runs carry an error tint and each row opens the run
// slide-over. Split out of `pages/processes.vue` (#1436); the page owns the
// filters, the fetch and the page number.
import type { TableColumn } from '@nuxt/ui'
import type { ProcessRun } from '~~/shared/types/ops'
import { formatDateTime, formatElapsed, formatNumber } from '~~/shared/utils/format'
import { processStatusColor, processStatusIcon } from '~~/shared/utils/pipeline-health'

defineProps<{
  runs: ProcessRun[]
  total: number
  pending: boolean
  /** The page the server actually served (its clamp wins over the model). */
  serverPage: number
  serverPageSize: number
  hasActiveFilters: boolean
  /** True when no time window is selected. */
  allTime: boolean
}>()

const page = defineModel<number>('page', { required: true })

defineEmits<{
  openRun: [run: ProcessRun]
}>()

const columns: TableColumn<ProcessRun>[] = [
  {
    accessorKey: 'processName',
    header: ({ column }) => sortableHeader(column, 'Process'),
  },
  {
    accessorKey: 'status',
    header: 'Status',
  },
  {
    accessorKey: 'startedAtUtc',
    header: ({ column }) => sortableHeader(column, 'Started'),
  },
  {
    accessorKey: 'durationMs',
    header: ({ column }) => sortableHeader(column, 'Duration', 'right'),
  },
  {
    accessorKey: 'host',
    header: 'Host',
  },
  {
    accessorKey: 'error',
    header: 'Error',
  },
  {
    id: 'actions',
    header: '',
  },
]

const sorting = ref([{ id: 'startedAtUtc', desc: true }])

// Tint rows by status: failed rows get an error tint, in-flight (Running) rows
// a subtle primary (rosegold) tint. Success and Skipped stay untinted — a skip
// is a settled, healthy outcome that simply did nothing. `meta.class.tr` is
// evaluated per row.
const tableMeta = {
  class: {
    tr: (row: { original: ProcessRun }) => {
      if (row.original.status === 'Failed') {
        return 'bg-error/5'
      }
      if (row.original.status === 'Abandoned') {
        return 'bg-warning/5'
      }
      if (row.original.status === 'Running') {
        return 'bg-primary/5'
      }
      return ''
    },
  },
}
</script>

<template>
  <UCard :ui="{ body: 'p-0 sm:p-0' }">
    <template #header>
      <div class="flex items-center justify-between gap-2">
        <div>
          <p class="text-sm font-medium text-highlighted">
            Recent runs
          </p>
          <p class="text-xs text-dimmed mt-0.5">
            {{ allTime
              ? 'Newest first, across all time.'
              : 'Newest first, in the selected window.' }}
          </p>
        </div>
        <UBadge
          v-if="!pending"
          color="neutral"
          variant="subtle"
          :label="`${formatNumber(total)} ${total === 1 ? 'run' : 'runs'}`"
        />
      </div>
    </template>

    <UTable
      v-model:sorting="sorting"
      :data="runs"
      :columns="columns"
      :meta="tableMeta"
      :loading="pending"
      loading-color="primary"
      :ui="{ td: 'py-2' }"
    >
      <template #processName-cell="{ row }">
        <span class="font-medium text-highlighted">
          {{ row.original.processName }}
        </span>
      </template>
      <template #status-cell="{ row }">
        <UBadge
          :color="processStatusColor(row.original.status)"
          :icon="processStatusIcon(row.original.status)"
          :ui="statusBadgeUi(row.original.status)"
          variant="subtle"
          size="sm"
          :label="row.original.status"
        />
      </template>
      <template #startedAtUtc-cell="{ row }">
        <span class="text-muted whitespace-nowrap">
          {{ formatDateTime(row.original.startedAtUtc) }}
        </span>
      </template>
      <template #durationMs-cell="{ row }">
        <div class="text-right tabular-nums whitespace-nowrap">
          <span v-if="row.original.status === 'Running'" class="text-dimmed">
            —
          </span>
          <template v-else>
            {{ formatElapsed(row.original.durationMs) }}
          </template>
        </div>
      </template>
      <template #host-cell="{ row }">
        <span class="text-muted font-mono text-xs">
          {{ row.original.host ?? '—' }}
        </span>
      </template>
      <template #error-cell="{ row }">
        <span
          v-if="row.original.error"
          class="text-error text-xs line-clamp-1 max-w-xs"
          :title="row.original.error"
        >
          {{ row.original.error }}
        </span>
        <span v-else class="text-dimmed">—</span>
      </template>
      <template #actions-cell="{ row }">
        <UButton
          icon="i-lucide-eye"
          color="neutral"
          variant="ghost"
          size="xs"
          aria-label="View run details"
          @click="$emit('openRun', row.original)"
        />
      </template>

      <template #empty>
        <div class="py-10 text-center text-sm text-muted">
          {{ hasActiveFilters ? 'No runs match these filters.' : 'No runs recorded yet.' }}
        </div>
      </template>
    </UTable>
  </UCard>

  <!-- Server-side pagination: total/page/pageSize come from the response. -->
  <div
    v-if="total > serverPageSize"
    class="flex items-center justify-between gap-2 mt-4"
  >
    <p class="text-xs text-muted tabular-nums">
      Page {{ serverPage.toLocaleString('en-US') }} of
      {{ Math.max(1, Math.ceil(total / serverPageSize)).toLocaleString('en-US') }}
    </p>
    <UPagination
      v-model:page="page"
      :total="total"
      :items-per-page="serverPageSize"
      :sibling-count="1"
      active-color="primary"
      variant="subtle"
      :disabled="pending"
    />
  </div>
</template>
