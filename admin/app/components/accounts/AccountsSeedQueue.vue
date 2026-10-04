<script setup lang="ts">
// Seed-request queue of the Accounts hub's "Add mains" tab: reflects requests from
// BOTH the single and the bulk flows, and from the weekly OTP seeder. Split out of
// `AccountsSeed.vue` (#1436); exposes `refresh`/`refreshFromFirstPage`/`pending`
// so the tab can refresh it after a submit and the hub's navbar button drives it.
import type { TableColumn } from '@nuxt/ui'
import type { SeedRequestReadModel, SeedRequestStatus } from '~~/shared/types/ops'
import { formatDateTime, formatNumber } from '~~/shared/utils/format'

// Server-paginated rather than a capped "recent" list (#1166): the weekly OTP
// seeder pushes tens of thousands of requests into this same queue in one run, so
// a list showing only its newest page showed a rounding error of its own contents
// and could not answer the question an operator actually has — how much is still
// pending.
const statusFilter = ref<'all' | SeedRequestStatus>(ALL)
const statusFilterItems = [
  { label: 'All statuses', value: ALL },
  { label: 'Pending', value: 'Pending' },
  { label: 'Resolving', value: 'Resolving' },
  { label: 'Ingested', value: 'Ingested' },
  { label: 'Failed', value: 'Failed' },
]
const regionFilter = ref<string>(ALL)
const searchFilter = ref('')
// Debounce the search so we don't fire a request per keystroke.
const searchDebounced = refDebounced(searchFilter, 300)
const page = ref(1)
const pageSize = 25

// Reset to page 1 whenever a filter narrows/widens the result set — otherwise a
// filter applied from page 40 lands on a page the new result set may not have.
watch([statusFilter, regionFilter, searchDebounced], () => {
  page.value = 1
})

const listFilters = computed(() => ({
  status: statusFilter.value === ALL ? undefined : statusFilter.value,
  region: regionFilter.value === ALL ? undefined : regionFilter.value,
  search: searchDebounced.value.trim() || undefined,
  page: page.value,
  pageSize,
}))

const hasFilters = computed(() =>
  statusFilter.value !== ALL
  || regionFilter.value !== ALL
  || Boolean(searchFilter.value.trim()),
)
function resetFilters() {
  statusFilter.value = ALL
  regionFilter.value = ALL
  searchFilter.value = ''
}

const { data, pending, error, refresh } = useSeedRequests(listFilters)

/**
 * Refresh after a submit. Jumps back to page 1 first: the list is newest-first, so
 * the request just created is on page 1 — refreshing in place would re-fetch
 * whichever page the operator was browsing and appear to have done nothing. Only
 * the submit paths use this; the poller refreshes in place, since yanking the page
 * out from under someone reading page 40 would be worse than a stale row.
 *
 * Either changes the page or refreshes, never both: `page` is part of the query key,
 * so assigning it already re-runs the fetch, and calling refresh() as well would
 * fire the same request twice.
 */
function refreshFromFirstPage() {
  if (page.value === 1) {
    refresh()
    return
  }

  page.value = 1
}

const requests = computed(() => data.value?.requests ?? [])
const total = computed(() => data.value?.total ?? 0)
const pageCount = computed(() => Math.max(1, Math.ceil(total.value / pageSize)))

const columns: TableColumn<SeedRequestReadModel>[] = [
  { accessorKey: 'gameName', header: 'Riot ID' },
  { accessorKey: 'platformId', header: 'Region' },
  { accessorKey: 'status', header: 'Status' },
  { accessorKey: 'requestedAtUtc', header: 'Requested' },
  { accessorKey: 'processedAtUtc', header: 'Processed' },
  { accessorKey: 'error', header: 'Error' },
]

const tableMeta = {
  class: {
    tr: (row: { original: SeedRequestReadModel }) =>
      row.original.status === 'Failed' ? 'bg-error/5' : '',
  },
}

defineExpose({ refresh, refreshFromFirstPage, pending })
</script>

<template>
  <section>
    <div class="flex flex-wrap items-center gap-2 mb-3">
      <p class="text-xs text-muted uppercase mr-auto">
        Seed request queue
      </p>
      <UInput
        v-model="searchFilter"
        icon="i-lucide-search"
        placeholder="Riot ID"
        class="w-full sm:w-64"
        :loading="pending"
      />
      <USelect
        v-model="statusFilter"
        :items="statusFilterItems"
        icon="i-lucide-check-circle"
        placeholder="Status"
        class="w-44"
      />
      <USelect
        v-model="regionFilter"
        :items="REGION_ITEMS"
        icon="i-lucide-globe"
        placeholder="Region"
        class="w-40"
      />
      <UButton
        v-if="hasFilters"
        icon="i-lucide-x"
        color="neutral"
        variant="ghost"
        label="Clear"
        @click="resetFilters()"
      />
    </div>

    <FetchErrorAlert
      v-if="error"
      :error="error"
      title="Failed to load seed requests"
      class="mb-4"
    />

    <UCard :ui="{ body: 'p-0 sm:p-0' }">
      <template #header>
        <div class="flex items-center justify-between gap-2">
          <p class="text-sm font-medium text-highlighted">
            History
          </p>
          <UBadge
            v-if="!pending"
            color="neutral"
            variant="subtle"
            :label="`${formatNumber(total)} ${total === 1 ? 'request' : 'requests'}`"
          />
        </div>
      </template>

      <UTable
        :data="requests"
        :columns="columns"
        :meta="tableMeta"
        :loading="pending"
        loading-color="primary"
        :ui="{ td: 'py-2' }"
      >
        <template #gameName-cell="{ row }">
          <span class="font-medium text-highlighted">
            {{ row.original.gameName }}<span class="text-dimmed">#{{ row.original.tagLine }}</span>
          </span>
        </template>
        <template #platformId-cell="{ row }">
          <span class="text-muted font-mono text-xs">
            {{ row.original.platformId }}
          </span>
        </template>
        <template #status-cell="{ row }">
          <UBadge
            :color="seedStatusColor(row.original.status)"
            :icon="seedStatusIcon(row.original.status)"
            variant="subtle"
            size="sm"
            :label="row.original.status"
          />
        </template>
        <template #requestedAtUtc-cell="{ row }">
          <span class="text-muted whitespace-nowrap">
            {{ formatDateTime(row.original.requestedAtUtc) }}
          </span>
        </template>
        <template #processedAtUtc-cell="{ row }">
          <span class="text-muted whitespace-nowrap">
            {{ formatDateTime(row.original.processedAtUtc) }}
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

        <template #empty>
          <div class="py-10 text-center text-sm text-muted">
            {{ hasFilters ? 'No seed requests match these filters.' : 'No seed requests yet.' }}
          </div>
        </template>
      </UTable>

      <!-- Pager -->
      <div
        v-if="total > pageSize"
        class="flex items-center justify-between gap-2 border-t border-default px-4 py-3"
      >
        <p class="text-xs text-muted tabular-nums">
          Page {{ page.toLocaleString('en-US') }} of {{ pageCount.toLocaleString('en-US') }}
        </p>
        <UPagination
          v-model:page="page"
          :total="total"
          :items-per-page="pageSize"
          :sibling-count="1"
          active-color="primary"
          variant="subtle"
          :disabled="pending"
          show-edges
        />
      </div>
    </UCard>
  </section>
</template>
