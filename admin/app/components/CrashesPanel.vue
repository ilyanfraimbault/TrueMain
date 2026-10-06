<script setup lang="ts">
// Crashes panel — server-paginated process crash reports from
// `GET /api/ops/crashes`, rendered as the "Crashes" tab of the Logs page. Each
// crash carries a server-derived plain-language explanation (#722) and is fully
// inspectable in a slide-over (exception chain, environment + memory/GC
// snapshot, and the log lines captured just before it); the whole report is
// copyable as text, and a checkbox selection can be copied as JSON.
import type { TableColumn } from '@nuxt/ui'
import type { CrashReport, CrashSource } from '~~/shared/types/ops'
import { formatDateTime } from '~~/shared/utils/format'

const processFilter = ref<string>(ALL)
const sourceFilter = ref<string>(ALL)
const sinceWindow = ref<SinceWindow>(ALL)
const searchInput = ref('')
const search = refDebounced(searchInput, 300)

const page = ref(1)
const pageSize = 25

// Freeze `since` when the window changes, not on every filters recompute — otherwise
// paging would re-sample Date.now() and drift the window forward between pages.
const sinceFrom = ref<string | undefined>(undefined)
watch(sinceWindow, (w) => {
  sinceFrom.value = w === ALL ? undefined : sinceToIso(w)
}, { immediate: true })

const filters = computed(() => ({
  process: processFilter.value === ALL ? undefined : processFilter.value,
  source: sourceFilter.value === ALL ? undefined : (sourceFilter.value as CrashSource),
  since: sinceFrom.value,
  search: search.value.trim() || undefined,
  page: page.value,
  pageSize,
}))

const hasActiveFilters = computed(() =>
  Boolean(
    processFilter.value !== ALL
    || sourceFilter.value !== ALL
    || sinceWindow.value !== ALL
    || searchInput.value.trim(),
  ),
)
function resetFilters() {
  processFilter.value = ALL
  sourceFilter.value = ALL
  sinceWindow.value = ALL
  searchInput.value = ''
}

const { data, pending, error, refresh } = useCrashes(filters)

const entries = computed(() => data.value?.entries ?? [])
const total = computed(() => data.value?.total ?? 0)
// The page the server actually served (its clamp wins over our optimistic ref).
const serverPage = computed(() => data.value?.page ?? page.value)
const serverPageSize = computed(() => data.value?.pageSize ?? pageSize)

// Filter options ride on every response (static backend catalogs), so no extra
// request is needed. Empty until the first response lands.
const processItems = computed(() => [
  { label: 'All processes', value: ALL },
  ...(data.value?.processes ?? []).map(name => ({ label: name, value: name })),
])
const sourceItems = computed(() => [
  { label: 'All sources', value: ALL },
  ...(data.value?.sources ?? []).map(name => ({ label: crashSourceLabel(name as CrashSource), value: name })),
])

// Any filter change must reset to the first page.
watch([processFilter, sourceFilter, sinceWindow, search], () => {
  page.value = 1
})

// --- Row selection (#722) ------------------------------------------------------
// Checkbox multi-select keyed by report id; cleared whenever the visible set
// changes (filter or page) so a hidden selection can't ride into the copied JSON.
const rowSelection = ref<Record<string, boolean>>({})
watch([filters], () => {
  rowSelection.value = {}
})

const selectedEntries = computed(() =>
  entries.value.filter(entry => rowSelection.value[entry.id]))
// Full reports, pretty-printed — not the truncated table cells.
const selectionJson = computed(() => JSON.stringify(selectedEntries.value, null, 2))

// --- Table -------------------------------------------------------------------
const columns: TableColumn<CrashReport>[] = [
  selectColumn<CrashReport>(),
  { accessorKey: 'timestampUtc', header: 'Time' },
  { accessorKey: 'processName', header: 'Process' },
  { accessorKey: 'source', header: 'Source' },
  { accessorKey: 'explanation', header: 'Explanation' },
  { accessorKey: 'exceptionType', header: 'Exception' },
  { accessorKey: 'message', header: 'Message' },
]

// Every recorded crash is a problem; tint each row so the list reads as one.
const tableMeta = {
  class: {
    tr: () => 'bg-error/5',
  },
}

// --- Detail slide-over -------------------------------------------------------
const detailOpen = ref(false)
const selectedEntry = ref<CrashReport | null>(null)
function openDetail(entry: CrashReport) {
  selectedEntry.value = entry
  detailOpen.value = true
}
</script>

<template>
  <div>
    <!-- Filters -->
    <div class="flex flex-wrap items-center gap-2 mb-4">
      <USelect
        v-model="processFilter"
        :items="processItems"
        icon="i-lucide-server"
        placeholder="Process"
        class="w-44"
      />
      <USelect
        v-model="sourceFilter"
        :items="sourceItems"
        icon="i-lucide-zap"
        placeholder="Source"
        class="w-52"
      />
      <USelect
        v-model="sinceWindow"
        :items="SINCE_ITEMS"
        icon="i-lucide-clock"
        placeholder="Since"
        class="w-44"
      />
      <UInput
        v-model="searchInput"
        icon="i-lucide-search"
        placeholder="Search message / stack…"
        class="w-64"
      />
      <UButton
        v-if="hasActiveFilters"
        icon="i-lucide-x"
        color="neutral"
        variant="ghost"
        label="Clear"
        @click="resetFilters"
      />
      <div class="flex-1" />
      <UButton
        icon="i-lucide-refresh-cw"
        color="neutral"
        variant="ghost"
        :loading="pending"
        aria-label="Refresh"
        @click="refresh()"
      />
    </div>

    <FetchErrorAlert
      v-if="error"
      :error="error"
      title="Failed to load crashes"
      class="mb-6"
    />

    <UCard :ui="{ body: 'p-0 sm:p-0' }">
      <template #header>
        <div class="flex items-center justify-between gap-2">
          <p class="text-sm font-medium text-highlighted">
            Crash reports
          </p>
          <div class="flex items-center gap-2">
            <CopyButton
              v-if="selectedEntries.length"
              :text="selectionJson"
              :label="`Copy JSON (${selectedEntries.length})`"
            />
            <UBadge
              v-if="!pending"
              color="neutral"
              variant="subtle"
              :label="`${total.toLocaleString('en-US')} ${total === 1 ? 'crash' : 'crashes'}`"
            />
          </div>
        </div>
      </template>

      <UTable
        v-model:row-selection="rowSelection"
        :data="entries"
        :columns="columns"
        :meta="tableMeta"
        :get-row-id="row => row.id"
        :loading="pending"
        loading-color="primary"
        :ui="{ td: 'py-2', tr: 'cursor-pointer' }"
        @select="(_event, row) => openDetail(row.original)"
      >
        <template #timestampUtc-cell="{ row }">
          <span class="text-muted whitespace-nowrap tabular-nums">
            {{ formatDateTime(row.original.timestampUtc) }}
          </span>
        </template>
        <template #processName-cell="{ row }">
          <UBadge color="neutral" variant="subtle" size="sm" :label="row.original.processName" />
        </template>
        <template #source-cell="{ row }">
          <UBadge
            :color="crashSourceColor(row.original.source)"
            :icon="crashSourceIcon(row.original.source)"
            variant="subtle"
            size="sm"
            :label="crashSourceLabel(row.original.source)"
          />
        </template>
        <template #explanation-cell="{ row }">
          <span
            class="text-xs line-clamp-2 max-w-[24rem] whitespace-normal"
            :title="row.original.explanation"
          >
            {{ row.original.explanation }}
          </span>
        </template>
        <template #exceptionType-cell="{ row }">
          <span
            v-if="row.original.exceptionType"
            class="font-mono text-xs text-muted line-clamp-1 max-w-[16rem]"
            :title="row.original.exceptionType"
          >
            {{ shortExceptionType(row.original.exceptionType) }}
          </span>
          <span v-else class="text-dimmed text-xs">—</span>
        </template>
        <template #message-cell="{ row }">
          <span
            class="font-mono text-xs line-clamp-1 max-w-[32rem]"
            :title="row.original.message ?? ''"
          >
            {{ row.original.message ?? '—' }}
          </span>
        </template>

        <template #empty>
          <div class="py-10 text-center text-sm text-muted">
            No crashes recorded for these filters — that's good news.
          </div>
        </template>
      </UTable>
    </UCard>

    <!-- Server-side pagination -->
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

    <!-- Crash detail slide-over -->
    <USlideover
      v-model:open="detailOpen"
      :title="selectedEntry ? crashSourceLabel(selectedEntry.source) : 'Crash'"
      :description="selectedEntry
        ? `${selectedEntry.processName} · ${formatDateTime(selectedEntry.timestampUtc)}`
        : ''"
    >
      <template #body>
        <CrashDetail v-if="selectedEntry" :entry="selectedEntry" />
      </template>
    </USlideover>
  </div>
</template>
