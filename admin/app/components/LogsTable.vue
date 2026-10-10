<script setup lang="ts">
// The Logs tab's table: one server page of `GET /api/ops/logs` entries, the
// checkbox selection copyable as JSON (#722), and a click on a row to open its
// detail. Split out of `pages/logs.vue` (#1436); the filters, the fetch and the
// selection state stay in `useLogsList`.
import type { TableColumn } from '@nuxt/ui'
import type { LogEntry } from '~~/shared/types/logs'
import { formatDateTime } from '~~/shared/utils/format'

defineProps<{
  entries: LogEntry[]
  pending: boolean
  total: number
  level: string
  selectedCount: number
  selectionJson: string
}>()

const emit = defineEmits<{
  select: [entry: LogEntry]
  showAllLevels: []
}>()

const rowSelection = defineModel<Record<string, boolean>>('rowSelection', { required: true })

const columns: TableColumn<LogEntry>[] = [
  selectColumn<LogEntry>(),
  { accessorKey: 'timestampUtc', header: 'Time' },
  { accessorKey: 'level', header: 'Level' },
  { accessorKey: 'eventType', header: 'Event' },
  { accessorKey: 'category', header: 'Category' },
  { accessorKey: 'message', header: 'Message' },
  { accessorKey: 'processName', header: 'Process' },
]

// Tint Error/Critical rows so failures stand out while scanning.
const tableMeta = {
  class: {
    tr: (row: { original: LogEntry }) =>
      row.original.level === 'Error' || row.original.level === 'Critical'
        ? 'bg-error/5'
        : '',
  },
}
</script>

<template>
  <UCard :ui="{ body: 'p-0 sm:p-0' }">
    <template #header>
      <div class="flex items-center justify-between gap-2">
        <p class="text-sm font-medium text-highlighted">
          Log entries
        </p>
        <div class="flex items-center gap-2">
          <CopyButton
            v-if="selectedCount"
            :text="selectionJson"
            :label="`Copy JSON (${selectedCount})`"
          />
          <UBadge
            v-if="!pending"
            color="neutral"
            variant="subtle"
            :label="`${total.toLocaleString('en-US')} ${total === 1 ? 'entry' : 'entries'}`"
          />
        </div>
      </div>
    </template>

    <UTable
      v-model:row-selection="rowSelection"
      :data="entries"
      :columns="columns"
      :meta="tableMeta"
      :get-row-id="row => String(row.id)"
      :loading="pending"
      loading-color="primary"
      :ui="{ td: 'py-2', tr: 'cursor-pointer' }"
      @select="(_event, row) => emit('select', row.original)"
    >
      <template #timestampUtc-cell="{ row }">
        <span class="text-muted whitespace-nowrap tabular-nums">
          {{ formatDateTime(row.original.timestampUtc) }}
        </span>
      </template>
      <template #level-cell="{ row }">
        <UBadge
          :color="levelColor(row.original.level)"
          :icon="levelIcon(row.original.level)"
          variant="subtle"
          size="sm"
          :label="row.original.level"
        />
      </template>
      <template #eventType-cell="{ row }">
        <UBadge
          v-if="row.original.eventType"
          color="primary"
          variant="subtle"
          size="sm"
          :label="row.original.eventType"
        />
        <span v-else class="text-dimmed text-xs">—</span>
      </template>
      <template #category-cell="{ row }">
        <span
          class="font-mono text-xs text-muted line-clamp-1 max-w-[16rem]"
          :title="row.original.category"
        >
          {{ row.original.category }}
        </span>
      </template>
      <template #message-cell="{ row }">
        <span
          class="font-mono text-xs line-clamp-1 max-w-[32rem]"
          :title="row.original.message"
        >
          {{ row.original.message }}
        </span>
      </template>
      <template #processName-cell="{ row }">
        <span class="text-muted font-mono text-xs whitespace-nowrap">
          {{ row.original.processName ?? '—' }}
        </span>
      </template>

      <template #empty>
        <div class="py-10 flex flex-col items-center gap-3">
          <p class="text-sm text-muted">
            No log entries match these filters.
          </p>
          <!-- Nothing at this severity is good news, but the operator
               still has to be able to see the quiet rows in one click. -->
          <UButton
            v-if="level !== ALL"
            icon="i-lucide-list"
            color="neutral"
            variant="subtle"
            size="sm"
            label="Show all levels"
            @click="emit('showAllLevels')"
          />
        </div>
      </template>
    </UTable>
  </UCard>
</template>
