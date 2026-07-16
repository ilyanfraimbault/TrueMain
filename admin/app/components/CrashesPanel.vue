<script setup lang="ts">
// Crashes panel — server-paginated process crash reports from
// `GET /api/ops/crashes`, rendered as the "Crashes" tab of the Logs page. Each
// crash is fully inspectable in a slide-over (exception chain, environment +
// memory/GC snapshot, and the log lines captured just before it); the whole report
// is copyable as text for pasting into an issue/chat.
import type { TableColumn } from '@nuxt/ui'
import type { BadgeColor, CrashReport, CrashSource } from '~~/shared/types/ops'
import { formatDateTime, formatDuration, humanizeBytes } from '~~/shared/utils/format'

const processFilter = ref<string>(ALL)
const sourceFilter = ref<string>(ALL)
const sinceWindow = ref<'all' | '1h' | '24h' | '7d' | '30d'>(ALL)
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
  ...(data.value?.sources ?? []).map(name => ({ label: sourceLabel(name as CrashSource), value: name })),
])

// Any filter change must reset to the first page.
watch([processFilter, sourceFilter, sinceWindow, search], () => {
  page.value = 1
})

// --- Source styling ----------------------------------------------------------
function sourceColor(s: CrashSource): BadgeColor {
  switch (s) {
    case 'UncleanShutdown':
    case 'HostRun':
    case 'AppDomainUnhandled':
      return 'error'
    case 'TaskSchedulerUnobserved':
      return 'warning'
    default:
      return 'neutral'
  }
}
function sourceIcon(s: CrashSource): string {
  switch (s) {
    case 'UncleanShutdown':
      return 'i-lucide-skull'
    case 'HostRun':
      return 'i-lucide-circle-x'
    case 'AppDomainUnhandled':
      return 'i-lucide-octagon-alert'
    case 'TaskSchedulerUnobserved':
      return 'i-lucide-triangle-alert'
    default:
      return 'i-lucide-bug'
  }
}
function sourceLabel(s: CrashSource): string {
  switch (s) {
    case 'UncleanShutdown':
      return 'Unclean shutdown'
    case 'HostRun':
      return 'Host run'
    case 'AppDomainUnhandled':
      return 'Unhandled exception'
    case 'TaskSchedulerUnobserved':
      return 'Unobserved task'
    default:
      return s
  }
}
const shortExceptionType = (full: string | null) =>
  full ? (full.split('.').pop() ?? full) : null

// --- Table -------------------------------------------------------------------
const columns: TableColumn<CrashReport>[] = [
  { accessorKey: 'timestampUtc', header: 'Time' },
  { accessorKey: 'processName', header: 'Process' },
  { accessorKey: 'source', header: 'Source' },
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

/** A human-readable, copy-pasteable rendering of the whole report. */
function crashToText(c: CrashReport): string {
  const lines: string[] = []
  lines.push(`Crash report — ${c.processName} — ${sourceLabel(c.source)}`)
  lines.push(`Time:      ${c.timestampUtc}`)
  if (c.exceptionType) {
    lines.push(`Exception: ${c.exceptionType}`)
  }
  if (c.message) {
    lines.push(`Message:   ${c.message}`)
  }
  lines.push(`Host:      ${c.host ?? '—'}`)
  lines.push(`OS:        ${c.osDescription ?? '—'}`)
  lines.push(`Runtime:   ${c.runtimeVersion ?? '—'}`)
  lines.push(`App:       ${c.appVersion ?? '—'}`)
  lines.push(`Uptime:    ${formatDuration(c.uptimeSeconds * 1000)}`)
  lines.push(`Memory:    working set ${humanizeBytes(c.workingSetBytes)}, managed heap ${humanizeBytes(c.totalManagedMemoryBytes)}`)
  lines.push(`GC:        gen0 ${c.gen0Collections} / gen1 ${c.gen1Collections} / gen2 ${c.gen2Collections}`)
  if (c.exitCode !== null) {
    lines.push(`Exit code: ${c.exitCode}`)
  }
  if (c.stackTrace) {
    lines.push('', 'Stack trace:', c.stackTrace)
  }
  if (c.innerExceptions.length) {
    lines.push('', 'Inner exceptions:')
    for (const e of c.innerExceptions) {
      lines.push(`  ${e.type}: ${e.message}`)
      if (e.stackTrace) {
        lines.push(e.stackTrace)
      }
    }
  }
  if (c.recentLogTail.length) {
    lines.push('', 'Recent log tail:')
    for (const t of c.recentLogTail) {
      lines.push(`  [${t.timestampUtc}] ${t.level} ${t.category}: ${t.message}`)
    }
  }
  return lines.join('\n')
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

    <UAlert
      v-if="error"
      color="error"
      variant="subtle"
      icon="i-lucide-triangle-alert"
      title="Failed to load crashes"
      :description="error.message"
      class="mb-6"
    />

    <UCard :ui="{ body: 'p-0 sm:p-0' }">
      <template #header>
        <div class="flex items-center justify-between gap-2">
          <p class="text-sm font-medium text-highlighted">
            Crash reports
          </p>
          <UBadge
            v-if="!pending"
            color="neutral"
            variant="subtle"
            :label="`${total.toLocaleString('en-US')} ${total === 1 ? 'crash' : 'crashes'}`"
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
        <template #processName-cell="{ row }">
          <UBadge color="neutral" variant="subtle" size="sm" :label="row.original.processName" />
        </template>
        <template #source-cell="{ row }">
          <UBadge
            :color="sourceColor(row.original.source)"
            :icon="sourceIcon(row.original.source)"
            variant="subtle"
            size="sm"
            :label="sourceLabel(row.original.source)"
          />
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
      :title="selectedEntry ? sourceLabel(selectedEntry.source) : 'Crash'"
      :description="selectedEntry
        ? `${selectedEntry.processName} · ${formatDateTime(selectedEntry.timestampUtc)}`
        : ''"
    >
      <template #body>
        <div v-if="selectedEntry" class="space-y-5">
          <div class="flex items-center gap-2">
            <CopyButton :text="crashToText(selectedEntry)" label="Copy report" />
            <UBadge
              :color="sourceColor(selectedEntry.source)"
              :icon="sourceIcon(selectedEntry.source)"
              variant="subtle"
              size="sm"
              :label="sourceLabel(selectedEntry.source)"
            />
          </div>

          <dl class="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
            <div>
              <dt class="text-muted text-xs uppercase mb-0.5">
                Process
              </dt>
              <dd class="font-mono text-xs">
                {{ selectedEntry.processName }}
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
            <div v-if="selectedEntry.exceptionType" class="col-span-2">
              <dt class="text-muted text-xs uppercase mb-0.5">
                Exception type
              </dt>
              <dd class="font-mono text-xs break-all">
                {{ selectedEntry.exceptionType }}
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
            <div>
              <dt class="text-muted text-xs uppercase mb-0.5">
                Uptime
              </dt>
              <dd class="tabular-nums">
                {{ formatDuration(selectedEntry.uptimeSeconds * 1000) }}
              </dd>
            </div>
            <div>
              <dt class="text-muted text-xs uppercase mb-0.5">
                Working set
              </dt>
              <dd class="tabular-nums">
                {{ humanizeBytes(selectedEntry.workingSetBytes) }}
              </dd>
            </div>
            <div>
              <dt class="text-muted text-xs uppercase mb-0.5">
                Managed heap
              </dt>
              <dd class="tabular-nums">
                {{ humanizeBytes(selectedEntry.totalManagedMemoryBytes) }}
              </dd>
            </div>
            <div>
              <dt class="text-muted text-xs uppercase mb-0.5">
                GC gen 0 / 1 / 2
              </dt>
              <dd class="tabular-nums">
                {{ selectedEntry.gen0Collections }} / {{ selectedEntry.gen1Collections }} / {{ selectedEntry.gen2Collections }}
              </dd>
            </div>
            <div v-if="selectedEntry.exitCode !== null">
              <dt class="text-muted text-xs uppercase mb-0.5">
                Exit code
              </dt>
              <dd class="tabular-nums">
                {{ selectedEntry.exitCode }}
              </dd>
            </div>
            <div class="col-span-2">
              <dt class="text-muted text-xs uppercase mb-0.5">
                Runtime / app version
              </dt>
              <dd class="font-mono text-xs break-all">
                {{ selectedEntry.runtimeVersion ?? '—' }} · {{ selectedEntry.appVersion ?? '—' }}
              </dd>
            </div>
            <div v-if="selectedEntry.osDescription" class="col-span-2">
              <dt class="text-muted text-xs uppercase mb-0.5">
                OS
              </dt>
              <dd class="font-mono text-xs break-all">
                {{ selectedEntry.osDescription }}
              </dd>
            </div>
          </dl>

          <div v-if="selectedEntry.message">
            <div class="flex items-center justify-between mb-1.5">
              <p class="text-muted text-xs uppercase">
                Message
              </p>
              <CopyButton :text="selectedEntry.message" label="Copy" />
            </div>
            <pre class="text-xs bg-elevated/50 border border-default rounded-md p-3 overflow-auto whitespace-pre-wrap">{{ selectedEntry.message }}</pre>
          </div>

          <div v-if="selectedEntry.stackTrace">
            <div class="flex items-center justify-between mb-1.5">
              <p class="text-muted text-xs uppercase">
                Stack trace
              </p>
              <CopyButton :text="selectedEntry.stackTrace" label="Copy" />
            </div>
            <pre class="text-xs text-error bg-error/5 border border-error/20 rounded-md p-3 overflow-auto whitespace-pre-wrap">{{ selectedEntry.stackTrace }}</pre>
          </div>

          <div v-if="selectedEntry.innerExceptions.length">
            <p class="text-muted text-xs uppercase mb-1.5">
              Inner exceptions
            </p>
            <div class="space-y-2">
              <div
                v-for="(inner, i) in selectedEntry.innerExceptions"
                :key="i"
                class="border border-default rounded-md p-3"
              >
                <p class="font-mono text-xs text-highlighted break-all mb-1">
                  {{ inner.type }}
                </p>
                <p class="text-xs text-muted mb-1">
                  {{ inner.message }}
                </p>
                <pre
                  v-if="inner.stackTrace"
                  class="text-xs text-muted bg-elevated/50 rounded p-2 overflow-auto whitespace-pre-wrap"
                >{{ inner.stackTrace }}</pre>
              </div>
            </div>
          </div>

          <div v-if="selectedEntry.recentLogTail.length">
            <p class="text-muted text-xs uppercase mb-1.5">
              Recent log tail ({{ selectedEntry.recentLogTail.length }})
            </p>
            <div class="border border-default rounded-md divide-y divide-default max-h-80 overflow-auto">
              <div
                v-for="(t, i) in selectedEntry.recentLogTail"
                :key="i"
                class="flex items-start gap-2 px-2 py-1 text-xs font-mono"
              >
                <span class="text-dimmed whitespace-nowrap tabular-nums">
                  {{ formatDateTime(t.timestampUtc) }}
                </span>
                <UBadge :color="levelColor(t.level)" variant="subtle" size="sm" :label="t.level" />
                <span class="text-muted break-all">{{ t.message }}</span>
              </div>
            </div>
          </div>
        </div>
      </template>
    </USlideover>
  </div>
</template>
