<script setup lang="ts">
// Database panel — sizes / row estimates from `GET /api/ops/db/tables`
// (returned total-bytes desc). Covers BOTH engines since #1023: Postgres tables
// and Mongo collections share one volume, so a Postgres-only list understated
// the disk and made the forecast optimistic by construction. Sortable table with
// humanized sizes plus a bar chart of the largest objects by total size.
//
// The page owns both fetches and hands them to the cards (`components/database/`):
// the history's error alert sits at the top of the page, above every card that
// reads it.
import type { DbTableRow } from '~~/shared/types/ops'
import { humanizeBytes } from '~~/shared/utils/format'

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

// --- Growth history + disk forecast (#925) -----------------------------------
const windowDays = ref(90)
const { data: history, pending: historyPending, error: historyError } = useDbStorageHistory(windowDays)
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
      <DatabaseForecast :history="history" :pending="historyPending" />

      <DatabaseGrowth v-model:window-days="windowDays" :history="history" :pending="historyPending" />

      <!-- Top tables by size -->
      <DatabaseTopTables :tables="data" :pending="pending" />

      <!-- Table list -->
      <DatabaseTablesTable :rows="rows" :pending="pending" />
    </template>
  </UDashboardPanel>
</template>
