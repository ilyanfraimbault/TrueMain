<script setup lang="ts">
// Logs panel — server-paginated application logs from `GET /api/ops/logs`.
// The filters, the fetch and the row selection live in `useLogsList`; the
// endpoint paginates, so UPagination is driven off the response's
// `total`/`page`/`pageSize`. Each row's full detail + exception stack is
// inspectable in a slide-over; rows are checkbox-selectable and the selection
// can be copied as JSON (#722, `LogsTable`).
import type { LogEntry } from '~~/shared/types/logs'
import { formatDateTime } from '~~/shared/utils/format'

// This page hosts two server-paginated lists: the application Logs and the durable
// Crashes list. A simple tab switch toggles between them; deep-linkable via
// ?view=crashes so a link can point straight at the Crashes tab.
const route = useRoute()
const router = useRouter()
const view = ref<'logs' | 'crashes'>(route.query.view === 'crashes' ? 'crashes' : 'logs')
watch(view, (v) => {
  router.replace({
    query: { ...route.query, view: v === 'crashes' ? 'crashes' : undefined },
  })
})
// The ⌘K palette can navigate to `/logs?view=crashes` while already on /logs —
// the component is reused, so setup doesn't re-run and only the query changes.
watch(() => route.query.view, (v) => {
  view.value = v === 'crashes' ? 'crashes' : 'logs'
})

const {
  level,
  eventType,
  process,
  exceptionsOnly,
  category,
  sinceWindow,
  searchInput,
  page,
  levelItems,
  eventItems,
  processItems,
  showAllLevels,
  hasActiveFilters,
  resetFilters,
  pending,
  error,
  refresh,
  entries,
  total,
  serverPage,
  serverPageSize,
  rowSelection,
  selectedEntries,
  selectionJson,
} = useLogsList()

// --- Detail slide-over -------------------------------------------------------
const detailOpen = ref(false)
const selectedEntry = ref<LogEntry | null>(null)
// Close any open detail when switching tabs so it doesn't reopen on return.
watch(view, () => {
  detailOpen.value = false
})
function openDetail(entry: LogEntry) {
  selectedEntry.value = entry
  detailOpen.value = true
}
</script>

<template>
  <UDashboardPanel id="logs">
    <template #header>
      <UDashboardNavbar title="Logs" icon="i-lucide-scroll-text">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            v-if="view === 'logs'"
            icon="i-lucide-refresh-cw"
            color="neutral"
            variant="ghost"
            :loading="pending"
            aria-label="Refresh"
            @click="refresh()"
          />
        </template>
      </UDashboardNavbar>

      <!-- Tab switch: Logs (this page's existing content) vs Crashes. -->
      <UDashboardToolbar>
        <template #left>
          <div class="flex items-center gap-1">
            <UButton
              :color="view === 'logs' ? 'primary' : 'neutral'"
              :variant="view === 'logs' ? 'solid' : 'ghost'"
              icon="i-lucide-scroll-text"
              label="Logs"
              @click="void (view = 'logs')"
            />
            <UButton
              :color="view === 'crashes' ? 'primary' : 'neutral'"
              :variant="view === 'crashes' ? 'solid' : 'ghost'"
              icon="i-lucide-skull"
              label="Crashes"
              @click="void (view = 'crashes')"
            />
          </div>
        </template>
      </UDashboardToolbar>

      <!-- `h-auto` + wrapping: seven filter controls no longer fit one fixed-height
           row, so let them flow onto a second line instead of clipping. -->
      <UDashboardToolbar v-if="view === 'logs'" :ui="{ root: 'h-auto py-2' }">
        <template #default>
          <div class="flex flex-wrap items-center gap-2 w-full">
            <USelect
              v-model="level"
              :items="levelItems"
              icon="i-lucide-bar-chart-3"
              placeholder="Level"
              class="w-40"
            />
            <USelect
              v-model="eventType"
              :items="eventItems"
              icon="i-lucide-zap"
              placeholder="Event"
              class="w-52"
            />
            <USelect
              v-model="process"
              :items="processItems"
              icon="i-lucide-server"
              placeholder="Process"
              class="w-40"
            />
            <USelect
              v-model="sinceWindow"
              :items="SINCE_ITEMS"
              icon="i-lucide-clock"
              placeholder="Since"
              class="w-44"
            />
            <UInput
              v-model="category"
              icon="i-lucide-folder"
              placeholder="Category…"
              class="w-56"
            />
            <UInput
              v-model="searchInput"
              icon="i-lucide-search"
              placeholder="Search message…"
              class="w-64"
            />
            <USwitch
              v-model="exceptionsOnly"
              label="Exceptions only"
              size="sm"
            />
            <div class="flex-1" />
            <UButton
              v-if="hasActiveFilters"
              icon="i-lucide-x"
              color="neutral"
              variant="ghost"
              label="Clear"
              @click="resetFilters"
            />
          </div>
        </template>
      </UDashboardToolbar>
    </template>

    <template #body>
      <CrashesPanel v-if="view === 'crashes'" />
      <template v-else>
        <FetchErrorAlert
          v-if="error"
          :error="error"
          title="Failed to load logs"
          class="mb-6"
        />

        <!-- Hint: level is a minimum threshold, not an exact match. -->
        <p v-if="level !== ALL" class="text-xs text-dimmed mb-3">
          Showing <span class="font-medium">{{ level }}</span> and above.
        </p>

        <LogsTable
          v-model:row-selection="rowSelection"
          :entries="entries"
          :pending="pending"
          :total="total"
          :level="level"
          :selected-count="selectedEntries.length"
          :selection-json="selectionJson"
          @select="openDetail"
          @show-all-levels="showAllLevels"
        />

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

        <!-- Entry detail slide-over -->
        <USlideover
          v-model:open="detailOpen"
          :title="selectedEntry?.level ?? 'Log entry'"
          :description="selectedEntry
            ? formatDateTime(selectedEntry.timestampUtc)
            : ''"
        >
          <template #body>
            <LogEntryDetail v-if="selectedEntry" :entry="selectedEntry" />
          </template>
        </USlideover>
      </template>
    </template>
  </UDashboardPanel>
</template>
