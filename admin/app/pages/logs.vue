<script setup lang="ts">
// Logs panel — server-paginated application logs from `GET /api/ops/logs`.
// Filterable by minimum severity (`level`), category, named ops event
// (`eventType`, exact match against the backend's static catalog), relative time
// window and a free-text search (matched against message + exception). The
// endpoint paginates, so we only ever hold one page in memory and drive
// UPagination off the response's `total`/`page`/`pageSize`. Each row's full
// detail + exception stack is inspectable in a slide-over.
import type { TableColumn } from '@nuxt/ui'
import type { LogEntry, LogLevel } from '~~/shared/types/ops'
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

// --- Filters -----------------------------------------------------------------
// Reka UI forbids an empty-string SelectItem value, so "All …" uses the
// non-empty `'all'` sentinel; `filters` maps it back to `undefined` (param
// omitted) so the backend still sees "no filter".
const ALL = 'all'
// `level` is a MINIMUM threshold: Warning returns Warning + Error + Critical.
const level = ref<'all' | LogLevel>(ALL)
// Named ops event (exact match); the option list comes from the response's
// static `eventTypes` catalog.
const eventType = ref<string>(ALL)
const category = ref('')
// Relative window -> ISO `since`. "All" omits the param.
const sinceWindow = ref<'all' | '1h' | '24h' | '7d' | '30d'>(ALL)
// Raw search input; debounced before it hits the query so typing doesn't fire a
// request per keystroke.
const searchInput = ref('')
const search = refDebounced(searchInput, 300)

const page = ref(1)
const pageSize = 50

const levelItems = [
  { label: 'All levels', value: ALL },
  { label: 'Trace', value: 'Trace' },
  { label: 'Debug', value: 'Debug' },
  { label: 'Information', value: 'Information' },
  { label: 'Warning', value: 'Warning' },
  { label: 'Error', value: 'Error' },
  { label: 'Critical', value: 'Critical' },
]
const sinceItems = [
  { label: 'All time', value: ALL },
  { label: 'Last hour', value: '1h' },
  { label: 'Last 24 hours', value: '24h' },
  { label: 'Last 7 days', value: '7d' },
  { label: 'Last 30 days', value: '30d' },
]

const WINDOW_MS: Record<string, number> = {
  '1h': 60 * 60 * 1000,
  '24h': 24 * 60 * 60 * 1000,
  '7d': 7 * 24 * 60 * 60 * 1000,
  '30d': 30 * 24 * 60 * 60 * 1000,
}

const filters = computed(() => ({
  level: level.value === ALL ? undefined : level.value,
  category: category.value.trim() || undefined,
  since: sinceWindow.value === ALL
    ? undefined
    : new Date(Date.now() - WINDOW_MS[sinceWindow.value]!).toISOString(),
  search: search.value.trim() || undefined,
  eventType: eventType.value === ALL ? undefined : eventType.value,
  page: page.value,
  pageSize,
}))

const hasActiveFilters = computed(() =>
  Boolean(
    level.value !== ALL
    || eventType.value !== ALL
    || category.value.trim()
    || sinceWindow.value !== ALL
    || searchInput.value.trim(),
  ),
)
function resetFilters() {
  level.value = ALL
  eventType.value = ALL
  category.value = ''
  sinceWindow.value = ALL
  searchInput.value = ''
}

const { data, pending, error, refresh } = useLogs(filters)

const entries = computed(() => data.value?.entries ?? [])
const total = computed(() => data.value?.total ?? 0)
// The page the server actually served (its clamp wins over our optimistic ref).
const serverPage = computed(() => data.value?.page ?? page.value)
const serverPageSize = computed(() => data.value?.pageSize ?? pageSize)

// Event filter options: the response carries the backend's static catalog of
// known event names on every page, so no extra request (or Mongo distinct) is
// needed. Empty until the first response lands.
const eventItems = computed(() => [
  { label: 'All events', value: ALL },
  ...(data.value?.eventTypes ?? []).map(name => ({ label: name, value: name })),
])

// Any filter change must reset to the first page — otherwise a narrower filter
// could leave us stranded on a now-out-of-range page. `search` (debounced) is
// watched rather than the raw input so the reset lands with the actual query.
watch([level, eventType, category, sinceWindow, search], () => {
  page.value = 1
})

// --- Severity styling --------------------------------------------------------
type BadgeColor = 'error' | 'warning' | 'success' | 'neutral'
function levelColor(l: LogLevel): BadgeColor {
  switch (l) {
    case 'Critical':
    case 'Error':
      return 'error'
    case 'Warning':
      return 'warning'
    case 'Information':
      return 'success'
    default:
      // Debug / Trace — muted.
      return 'neutral'
  }
}
function levelIcon(l: LogLevel): string {
  switch (l) {
    case 'Critical':
      return 'i-lucide-octagon-alert'
    case 'Error':
      return 'i-lucide-circle-x'
    case 'Warning':
      return 'i-lucide-triangle-alert'
    case 'Information':
      return 'i-lucide-info'
    default:
      return 'i-lucide-bug'
  }
}

// --- Table -------------------------------------------------------------------
const columns: TableColumn<LogEntry>[] = [
  {
    accessorKey: 'timestampUtc',
    header: 'Time',
  },
  {
    accessorKey: 'level',
    header: 'Level',
  },
  {
    accessorKey: 'eventType',
    header: 'Event',
  },
  {
    accessorKey: 'category',
    header: 'Category',
  },
  {
    accessorKey: 'message',
    header: 'Message',
  },
  {
    accessorKey: 'processName',
    header: 'Process',
  },
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

// --- Detail slide-over -------------------------------------------------------
const detailOpen = ref(false)
const selectedEntry = ref<LogEntry | null>(null)
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
              @click="view = 'logs'"
            />
            <UButton
              :color="view === 'crashes' ? 'primary' : 'neutral'"
              :variant="view === 'crashes' ? 'solid' : 'ghost'"
              icon="i-lucide-skull"
              label="Crashes"
              @click="view = 'crashes'"
            />
          </div>
        </template>
      </UDashboardToolbar>

      <UDashboardToolbar v-if="view === 'logs'">
        <template #left>
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
            v-model="sinceWindow"
            :items="sinceItems"
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
        </template>
        <template #right>
          <UButton
            v-if="hasActiveFilters"
            icon="i-lucide-x"
            color="neutral"
            variant="ghost"
            label="Clear"
            @click="resetFilters"
          />
        </template>
      </UDashboardToolbar>
    </template>

    <template #body>
      <CrashesPanel v-if="view === 'crashes'" />
      <template v-else>
      <UAlert
        v-if="error"
        color="error"
        variant="subtle"
        icon="i-lucide-triangle-alert"
        title="Failed to load logs"
        :description="error.message"
        class="mb-6"
      />

      <!-- Hint: level is a minimum threshold, not an exact match. -->
      <p v-if="level !== ALL" class="text-xs text-dimmed mb-3">
        Showing <span class="font-medium">{{ level }}</span> and above.
      </p>

      <UCard :ui="{ body: 'p-0 sm:p-0' }">
        <template #header>
          <div class="flex items-center justify-between gap-2">
            <p class="text-sm font-medium text-highlighted">
              Log entries
            </p>
            <UBadge
              v-if="!pending"
              color="neutral"
              variant="subtle"
              :label="`${total.toLocaleString('en-US')} ${total === 1 ? 'entry' : 'entries'}`"
            />
          </div>
        </template>

        <UTable
          :data="entries"
          :columns="columns"
          :meta="tableMeta"
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
            <div class="py-10 text-center text-sm text-muted">
              No log entries match these filters.
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

      <!-- Entry detail slide-over -->
      <USlideover
        v-model:open="detailOpen"
        :title="selectedEntry?.level ?? 'Log entry'"
        :description="selectedEntry
          ? formatDateTime(selectedEntry.timestampUtc)
          : ''"
      >
        <template #body>
          <div v-if="selectedEntry" class="space-y-5">
            <dl class="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">
                  Level
                </dt>
                <dd>
                  <UBadge
                    :color="levelColor(selectedEntry.level)"
                    :icon="levelIcon(selectedEntry.level)"
                    variant="subtle"
                    size="sm"
                    :label="selectedEntry.level"
                  />
                </dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">
                  Timestamp
                </dt>
                <dd class="tabular-nums">
                  {{ formatDateTime(selectedEntry.timestampUtc) }}
                </dd>
              </div>
              <div class="col-span-2">
                <dt class="text-muted text-xs uppercase mb-0.5">
                  Category
                </dt>
                <dd class="font-mono text-xs break-all">
                  {{ selectedEntry.category }}
                </dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">
                  Process
                </dt>
                <dd class="font-mono text-xs">
                  {{ selectedEntry.processName ?? '—' }}
                </dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">
                  Host
                </dt>
                <dd class="font-mono text-xs">
                  {{ selectedEntry.host ?? '—' }}
                </dd>
              </div>
              <div v-if="selectedEntry.eventType" class="col-span-2">
                <dt class="text-muted text-xs uppercase mb-0.5">
                  Event
                </dt>
                <dd>
                  <UBadge
                    color="primary"
                    variant="subtle"
                    size="sm"
                    :label="selectedEntry.eventType"
                  />
                </dd>
              </div>
            </dl>

            <div>
              <div class="flex items-center justify-between mb-1.5">
                <p class="text-muted text-xs uppercase">
                  Message
                </p>
                <CopyButton :text="selectedEntry.message" label="Copy" />
              </div>
              <pre class="text-xs bg-elevated/50 border border-default rounded-md p-3 overflow-auto whitespace-pre-wrap">{{ selectedEntry.message }}</pre>
            </div>

            <div v-if="selectedEntry.exception">
              <div class="flex items-center justify-between mb-1.5">
                <p class="text-muted text-xs uppercase">
                  Exception
                </p>
                <CopyButton :text="selectedEntry.exception" label="Copy" />
              </div>
              <pre class="text-xs text-error bg-error/5 border border-error/20 rounded-md p-3 overflow-auto whitespace-pre-wrap">{{ selectedEntry.exception }}</pre>
            </div>
          </div>
        </template>
      </USlideover>
      </template>
    </template>
  </UDashboardPanel>
</template>
