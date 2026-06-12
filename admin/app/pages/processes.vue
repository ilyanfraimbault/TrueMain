<script setup lang="ts">
// Processes panel — background-job run health from `GET /api/ops/process-runs`.
// A per-process rollup (last status / last run / last success / recent failures)
// plus a filterable, server-paginated runs table. The endpoint paginates, so we
// only ever hold one page in memory and drive UPagination off the response's
// `total`/`page`/`pageSize` (the rollup covers the full filtered set, not the
// page). Failed runs are visually distinct (error tint) and each run's
// `summary` JSON + error is inspectable in a slide-over.
import type { TableColumn } from '@nuxt/ui'
import type { ProcessIteration, ProcessRollup, ProcessRun, ProcessRunStatus } from '~~/shared/types/ops'
import { PIPELINE_CHAIN } from '~~/shared/types/ops'
import { formatDateTime, formatDuration, formatNumber } from '~~/shared/utils/format'

// --- Filters -----------------------------------------------------------------
// Reka UI forbids an empty-string SelectItem value, so "All …" uses the
// non-empty `'all'` sentinel; `filters` maps it back to `undefined` (param
// omitted) so the backend still sees "no filter".
const ALL = 'all'
const processName = ref('')
const status = ref<'all' | ProcessRunStatus>(ALL)
// Relative window -> ISO `since`. "All" omits the param.
const sinceWindow = ref<'all' | '1h' | '24h' | '7d' | '30d'>(ALL)

const statusItems = [
  { label: 'All statuses', value: ALL },
  { label: 'Running', value: 'Running' },
  { label: 'Success', value: 'Success' },
  { label: 'Failed', value: 'Failed' },
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

// Default view = the most recent runs regardless of recency: send NO `since`
// lower bound. The backend applies no time floor unless `since` is explicitly
// provided, so older-but-real runs still show up (this fixes the misleading
// "0 runs" when nothing ran recently). The time-window select is purely an
// OPTIONAL filter: "All time" omits `since`; the relative windows send their
// computed `since`.
const page = ref(1)
const pageSize = 50

const filters = computed(() => ({
  processName: processName.value.trim() || undefined,
  status: status.value === ALL ? undefined : status.value,
  since: sinceWindow.value === ALL
    ? undefined
    : new Date(Date.now() - WINDOW_MS[sinceWindow.value]!).toISOString(),
  page: page.value,
  pageSize,
}))

const hasActiveFilters = computed(() =>
  Boolean(
    processName.value.trim()
    || status.value !== ALL
    || sinceWindow.value !== ALL,
  ),
)
function resetFilters() {
  processName.value = ''
  status.value = ALL
  sinceWindow.value = ALL
}

const { data, pending, error, refresh } = useProcessRuns(filters)

const rollup = computed(() => data.value?.rollup ?? [])
const runs = computed(() => data.value?.runs ?? [])
const total = computed(() => data.value?.total ?? 0)
// The page the server actually served (its clamp wins over our optimistic ref).
const serverPage = computed(() => data.value?.page ?? page.value)
const serverPageSize = computed(() => data.value?.pageSize ?? pageSize)

// Any filter change must reset to the first page — otherwise a narrower filter
// could leave us stranded on a now-out-of-range page.
watch([processName, status, sinceWindow], () => {
  page.value = 1
})

// Running uses the emerald `primary` accent (in-flight, not yet an outcome);
// Success is `success`, everything else (Failed) is `error`.
function statusColor(s: ProcessRunStatus): 'primary' | 'success' | 'error' {
  if (s === 'Running') {
    return 'primary'
  }
  return s === 'Success' ? 'success' : 'error'
}
function statusIcon(s: ProcessRunStatus): string {
  if (s === 'Running') {
    return 'i-lucide-loader-circle'
  }
  return s === 'Success' ? 'i-lucide-circle-check' : 'i-lucide-circle-x'
}

// Whether any process currently has an in-flight (Running) latest run, surfaced
// as a small banner above the rollup so operators see "what's running now".
const runningProcesses = computed(() =>
  rollup.value.filter(proc => proc.lastStatus === 'Running'),
)

// --- Pipeline chain + iterations ---------------------------------------------
// Recent iterations (one full pass of the chain each), newest first, with their
// per-process runs. Paged independently of the runs table below.
const iterationsPage = ref(1)
const iterationsPageSize = 8
const iterationsFilters = computed(() => ({
  page: iterationsPage.value,
  pageSize: iterationsPageSize,
}))
const {
  data: iterationsData,
  pending: iterationsPending,
  error: iterationsError,
} = useProcessIterations(iterationsFilters)

const iterations = computed<ProcessIteration[]>(() => iterationsData.value?.iterations ?? [])
const iterationsTotal = computed(() => iterationsData.value?.total ?? 0)
const iterationsServerPage = computed(() => iterationsData.value?.page ?? iterationsPage.value)

// Per-process outcome within one iteration. `notRun` covers both a process that
// hasn't started yet in the current pass and one that was skipped this pass.
type ChainOutcome = ProcessRunStatus | 'notRun'

interface ChainLink {
  processName: string
  outcome: ChainOutcome
  run: ProcessRun | null
}

// Build the canonical ordered chain for one iteration: every process in
// PIPELINE_CHAIN, annotated with its run outcome (or `notRun` when absent).
function buildChain(runs: ProcessRun[]): ChainLink[] {
  const byName = new Map(runs.map(run => [run.processName, run]))
  return PIPELINE_CHAIN.map((processName) => {
    const run = byName.get(processName) ?? null
    return {
      processName,
      outcome: run?.status ?? 'notRun',
      run,
    }
  })
}

// The live chain shown at the top: the newest iteration's outcomes when present,
// otherwise the bare canonical chain with everything not-yet-run. This is what
// highlights "where we currently are".
const currentChain = computed<ChainLink[]>(() => {
  const newest = iterations.value[0]
  return buildChain(newest?.runs ?? [])
})
const currentIterationRunning = computed(() => iterations.value[0]?.isRunning ?? false)

function outcomeColor(outcome: ChainOutcome): 'primary' | 'success' | 'error' | 'neutral' {
  switch (outcome) {
    case 'Running':
      return 'primary'
    case 'Success':
      return 'success'
    case 'Failed':
      return 'error'
    default:
      return 'neutral'
  }
}
function outcomeIcon(outcome: ChainOutcome): string {
  switch (outcome) {
    case 'Running':
      return 'i-lucide-loader-circle'
    case 'Success':
      return 'i-lucide-circle-check'
    case 'Failed':
      return 'i-lucide-circle-x'
    default:
      return 'i-lucide-circle-dashed'
  }
}
function outcomeLabel(outcome: ChainOutcome): string {
  return outcome === 'notRun' ? 'Not run' : outcome
}

// Short process labels keep the chain compact (e.g. "ChampionPatternAggregation"
// → "Pattern Agg.") without losing meaning.
const CHAIN_LABELS: Record<string, string> = {
  Discovery: 'Discovery',
  ManualSeed: 'Manual Seed',
  Scoring: 'Scoring',
  MatchIngestion: 'Match Ingest',
  MainAnalysis: 'Main Analysis',
  ChampionPatternAggregation: 'Pattern Agg.',
  AccountRefresh: 'Acct Refresh',
  MatchDataRetention: 'Retention',
}
function chainLabel(processName: string): string {
  return CHAIN_LABELS[processName] ?? processName
}

// Failure-cell coloring. The window always holds thousands of failures, so the
// raw count is always > 0 and coloring by it would keep the cell permanently
// red (the bug). Color instead by a meaningful signal:
//   - the latest run is currently failing  -> error (the process is unhealthy now)
//   - else a high recent failure *rate*     -> warning (degraded but recovered)
//   - else                                  -> neutral
// `failureRateInWindow` is a real ratio (failures / runs in window), not a
// fabricated metric. The 0.25 threshold is a display cue, not a published stat.
const HIGH_FAILURE_RATE = 0.25
function failureTextClass(proc: ProcessRollup): string {
  if (proc.lastStatus === 'Failed') {
    return 'text-error'
  }
  if (proc.failureRateInWindow >= HIGH_FAILURE_RATE) {
    return 'text-warning'
  }
  return 'text-default'
}
function failureRateLabel(proc: ProcessRollup): string {
  if (proc.runCountInWindow === 0) {
    return '—'
  }
  // floor, not round: never show 100% while the window still has a success.
  return `${Math.floor(proc.failureRateInWindow * 100)}% of ${formatNumber(proc.runCountInWindow)}`
}

// --- Runs table --------------------------------------------------------------
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
// a subtle emerald tint. `meta.class.tr` is a function evaluated per row.
const tableMeta = {
  class: {
    tr: (row: { original: ProcessRun }) => {
      if (row.original.status === 'Failed') {
        return 'bg-error/5'
      }
      if (row.original.status === 'Running') {
        return 'bg-primary/5'
      }
      return ''
    },
  },
}

// --- Summary slide-over ------------------------------------------------------
const detailOpen = ref(false)
const selectedRun = ref<ProcessRun | null>(null)
function openDetail(run: ProcessRun) {
  selectedRun.value = run
  detailOpen.value = true
}
const selectedSummaryJson = computed(() => {
  const s = selectedRun.value?.summary
  if (s === null || s === undefined) {
    return null
  }
  return JSON.stringify(s, null, 2)
})
</script>

<template>
  <UDashboardPanel id="processes">
    <template #header>
      <UDashboardNavbar title="Processes" icon="i-lucide-activity">
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
            v-model="processName"
            icon="i-lucide-search"
            placeholder="Process name…"
            class="w-56"
          />
          <USelect
            v-model="status"
            :items="statusItems"
            icon="i-lucide-check-circle"
            placeholder="Status"
            class="w-44"
          />
          <USelect
            v-model="sinceWindow"
            :items="sinceItems"
            icon="i-lucide-clock"
            placeholder="Since"
            class="w-44"
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
      <UAlert
        v-if="error"
        color="error"
        variant="subtle"
        icon="i-lucide-triangle-alert"
        title="Failed to load process runs"
        :description="error.message"
        class="mb-6"
      />

      <!-- Running-now banner: which process(es) are currently in flight. The
           Running row IS the live state, so this reflects real data only. -->
      <div
        v-if="runningProcesses.length > 0"
        class="mb-6 flex items-center gap-3 rounded-lg border border-primary/40 bg-primary/5 px-4 py-3"
      >
        <UIcon
          name="i-lucide-loader-circle"
          class="size-5 text-primary animate-spin shrink-0"
        />
        <div class="text-sm">
          <span class="font-medium text-highlighted">
            {{ runningProcesses.length === 1 ? 'Running now' : `${runningProcesses.length} running now` }}
          </span>
          <span class="text-muted">
            · {{ runningProcesses.map(proc => proc.processName).join(', ') }}
          </span>
        </div>
      </div>

      <!-- Pipeline chain: the canonical ordered chain with the current position
           highlighted (the Running link). Reflects the newest iteration's
           per-process outcomes, or a bare not-yet-run chain when none exist. -->
      <div class="mb-6">
        <div class="flex items-center justify-between gap-2 mb-3">
          <p class="text-xs text-muted uppercase">
            Pipeline chain
          </p>
          <span
            v-if="currentIterationRunning"
            class="inline-flex items-center gap-1.5 text-xs text-primary font-medium"
          >
            <UIcon name="i-lucide-loader-circle" class="size-3.5 animate-spin" />
            In progress
          </span>
        </div>

        <div
          v-if="iterationsError"
          class="py-6 text-center text-sm text-muted border border-default rounded-lg"
        >
          Failed to load the pipeline chain.
        </div>
        <div
          v-else
          class="flex flex-wrap items-center gap-y-2 rounded-lg border border-default bg-elevated/25 p-4"
        >
          <template v-for="(link, i) in currentChain" :key="link.processName">
            <button
              type="button"
              class="group inline-flex items-center gap-2 rounded-md border px-2.5 py-1.5 transition-colors"
              :class="{
                'border-primary/50 bg-primary/10': link.outcome === 'Running',
                'border-success/30 bg-success/5': link.outcome === 'Success',
                'border-error/40 bg-error/10': link.outcome === 'Failed',
                'border-default bg-default opacity-60': link.outcome === 'notRun',
                'cursor-default': !link.run,
              }"
              :disabled="!link.run"
              :title="link.run ? 'View run details' : outcomeLabel(link.outcome)"
              @click="link.run && openDetail(link.run)"
            >
              <UIcon
                :name="outcomeIcon(link.outcome)"
                class="size-4 shrink-0"
                :class="{
                  'text-primary animate-spin': link.outcome === 'Running',
                  'text-success': link.outcome === 'Success',
                  'text-error': link.outcome === 'Failed',
                  'text-dimmed': link.outcome === 'notRun',
                }"
              />
              <span
                class="text-xs font-medium whitespace-nowrap"
                :class="link.outcome === 'notRun' ? 'text-dimmed' : 'text-highlighted'"
              >
                {{ chainLabel(link.processName) }}
              </span>
            </button>
            <UIcon
              v-if="i < currentChain.length - 1"
              name="i-lucide-chevron-right"
              class="size-4 text-dimmed shrink-0 mx-0.5"
            />
          </template>
        </div>
      </div>

      <!-- Recent iterations: each iteration as the chain with per-process
           outcomes. Newest first. -->
      <div class="mb-6">
        <p class="text-xs text-muted uppercase mb-3">
          Recent iterations
        </p>

        <div v-if="iterationsPending" class="space-y-3">
          <USkeleton v-for="i in 3" :key="i" class="h-16 w-full rounded-lg" />
        </div>
        <div
          v-else-if="iterations.length === 0"
          class="py-8 text-center text-sm text-muted border border-default rounded-lg"
        >
          No pipeline iterations recorded yet.
        </div>
        <div v-else class="space-y-3">
          <div
            v-for="iteration in iterations"
            :key="iteration.iterationId"
            class="rounded-lg border p-4 bg-elevated/25"
            :class="iteration.isRunning ? 'border-primary/40' : 'border-default'"
          >
            <div class="flex items-center justify-between gap-2 mb-3">
              <div class="flex items-center gap-2 text-sm">
                <UIcon
                  v-if="iteration.isRunning"
                  name="i-lucide-loader-circle"
                  class="size-4 text-primary animate-spin shrink-0"
                />
                <span class="text-muted">
                  {{ formatDateTime(iteration.startedAtUtc) }}
                </span>
              </div>
              <UBadge
                v-if="iteration.isRunning"
                color="primary"
                variant="subtle"
                size="sm"
                label="Running"
              />
            </div>

            <div class="flex flex-wrap items-center gap-y-2">
              <template
                v-for="(link, i) in buildChain(iteration.runs)"
                :key="link.processName"
              >
                <button
                  type="button"
                  class="inline-flex items-center gap-1.5 rounded-md border px-2 py-1 transition-colors"
                  :class="{
                    'border-primary/50 bg-primary/10': link.outcome === 'Running',
                    'border-success/30 bg-success/5': link.outcome === 'Success',
                    'border-error/40 bg-error/10': link.outcome === 'Failed',
                    'border-default bg-default opacity-50': link.outcome === 'notRun',
                    'cursor-default': !link.run,
                  }"
                  :disabled="!link.run"
                  :title="`${link.processName} · ${outcomeLabel(link.outcome)}`"
                  @click="link.run && openDetail(link.run)"
                >
                  <UIcon
                    :name="outcomeIcon(link.outcome)"
                    class="size-3.5 shrink-0"
                    :class="{
                      'text-primary animate-spin': link.outcome === 'Running',
                      'text-success': link.outcome === 'Success',
                      'text-error': link.outcome === 'Failed',
                      'text-dimmed': link.outcome === 'notRun',
                    }"
                  />
                  <span
                    class="text-[11px] font-medium whitespace-nowrap"
                    :class="link.outcome === 'notRun' ? 'text-dimmed' : 'text-default'"
                  >
                    {{ chainLabel(link.processName) }}
                  </span>
                </button>
                <UIcon
                  v-if="i < PIPELINE_CHAIN.length - 1"
                  name="i-lucide-chevron-right"
                  class="size-3.5 text-dimmed shrink-0 mx-0.5"
                />
              </template>
            </div>
          </div>
        </div>

        <!-- Iteration pagination -->
        <div
          v-if="iterationsTotal > iterationsPageSize"
          class="flex items-center justify-between gap-2 mt-4"
        >
          <p class="text-xs text-muted tabular-nums">
            Page {{ iterationsServerPage.toLocaleString('en-US') }} of
            {{ Math.max(1, Math.ceil(iterationsTotal / iterationsPageSize)).toLocaleString('en-US') }}
          </p>
          <UPagination
            v-model:page="iterationsPage"
            :total="iterationsTotal"
            :items-per-page="iterationsPageSize"
            :sibling-count="1"
            active-color="primary"
            variant="subtle"
            :disabled="iterationsPending"
          />
        </div>
      </div>

      <!-- Rollup: one card per process -->
      <div class="mb-6">
        <p class="text-xs text-muted uppercase mb-3">
          Process health
        </p>

        <div
          v-if="pending"
          class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4"
        >
          <USkeleton v-for="i in 3" :key="i" class="h-28 w-full rounded-lg" />
        </div>
        <div
          v-else-if="rollup.length === 0"
          class="py-10 text-center text-sm text-muted border border-default rounded-lg"
        >
          No process runs recorded yet.
        </div>
        <div
          v-else
          class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4"
        >
          <div
            v-for="proc in rollup"
            :key="proc.processName"
            class="rounded-lg border p-4 bg-elevated/25"
            :class="{
              'border-error/30': proc.lastStatus === 'Failed',
              'border-primary/40': proc.lastStatus === 'Running',
              'border-default': proc.lastStatus !== 'Failed' && proc.lastStatus !== 'Running',
            }"
          >
            <div class="flex items-center justify-between gap-2 mb-3">
              <p class="font-medium text-highlighted truncate">
                {{ proc.processName }}
              </p>
              <UBadge
                :color="statusColor(proc.lastStatus)"
                :icon="statusIcon(proc.lastStatus)"
                variant="subtle"
                size="sm"
                :label="proc.lastStatus"
              />
            </div>
            <dl class="space-y-1.5 text-sm">
              <div class="flex justify-between gap-2">
                <dt class="text-muted">
                  Last run
                </dt>
                <dd class="text-default text-right">
                  {{ formatDateTime(proc.lastRunAtUtc) }}
                </dd>
              </div>
              <div class="flex justify-between gap-2">
                <dt class="text-muted">
                  Last success
                </dt>
                <dd class="text-default text-right">
                  {{ formatDateTime(proc.lastSuccessAtUtc) }}
                </dd>
              </div>
              <div class="flex justify-between gap-2">
                <dt class="text-muted">
                  {{ sinceWindow === ALL ? 'Failures (all time)' : 'Failures (window)' }}
                </dt>
                <dd class="text-right">
                  <span
                    class="tabular-nums font-medium"
                    :class="failureTextClass(proc)"
                  >
                    {{ formatNumber(proc.failureCountInWindow) }}
                  </span>
                  <span class="block text-xs text-dimmed tabular-nums">
                    {{ failureRateLabel(proc) }}
                  </span>
                </dd>
              </div>
            </dl>
          </div>
        </div>
      </div>

      <!-- Runs table -->
      <UCard :ui="{ body: 'p-0 sm:p-0' }">
        <template #header>
          <div class="flex items-center justify-between gap-2">
            <div>
              <p class="text-sm font-medium text-highlighted">
                Recent runs
              </p>
              <p class="text-xs text-dimmed mt-0.5">
                {{ sinceWindow === ALL
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
              :color="statusColor(row.original.status)"
              :icon="statusIcon(row.original.status)"
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
                {{ formatDuration(row.original.durationMs) }}
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
              @click="openDetail(row.original)"
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

      <!-- Run detail slide-over -->
      <USlideover
        v-model:open="detailOpen"
        :title="selectedRun?.processName ?? 'Run details'"
        :description="selectedRun
          ? `${selectedRun.status} · ${formatDateTime(selectedRun.startedAtUtc)}`
          : ''"
      >
        <template #body>
          <div v-if="selectedRun" class="space-y-5">
            <dl class="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">
                  Status
                </dt>
                <dd>
                  <UBadge
                    :color="statusColor(selectedRun.status)"
                    :icon="statusIcon(selectedRun.status)"
                    variant="subtle"
                    size="sm"
                    :label="selectedRun.status"
                  />
                </dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">
                  Duration
                </dt>
                <dd class="tabular-nums">
                  <span v-if="selectedRun.status === 'Running'" class="text-dimmed">
                    In progress…
                  </span>
                  <template v-else>
                    {{ formatDuration(selectedRun.durationMs) }}
                  </template>
                </dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">
                  Started
                </dt>
                <dd>{{ formatDateTime(selectedRun.startedAtUtc) }}</dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">
                  Finished
                </dt>
                <dd>
                  <span v-if="selectedRun.status === 'Running'" class="text-dimmed">
                    —
                  </span>
                  <template v-else>
                    {{ formatDateTime(selectedRun.finishedAtUtc) }}
                  </template>
                </dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">
                  Host
                </dt>
                <dd class="font-mono text-xs">
                  {{ selectedRun.host ?? '—' }}
                </dd>
              </div>
            </dl>

            <div v-if="selectedRun.error">
              <p class="text-muted text-xs uppercase mb-1.5">
                Error
              </p>
              <pre class="text-xs text-error bg-error/5 border border-error/20 rounded-md p-3 overflow-auto whitespace-pre-wrap">{{ selectedRun.error }}</pre>
            </div>

            <div>
              <p class="text-muted text-xs uppercase mb-1.5">
                Summary
              </p>
              <pre
                v-if="selectedSummaryJson"
                class="text-xs bg-elevated/50 border border-default rounded-md p-3 overflow-auto"
              >{{ selectedSummaryJson }}</pre>
              <p v-else class="text-sm text-dimmed">
                No summary recorded for this run.
              </p>
            </div>
          </div>
        </template>
      </USlideover>
    </template>
  </UDashboardPanel>
</template>
