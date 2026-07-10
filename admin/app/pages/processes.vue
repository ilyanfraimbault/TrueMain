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
  { label: 'Abandoned', value: 'Abandoned' },
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
// Success is `success`, Abandoned is `warning` (orphaned, not a clean fail),
// Failed is `error`.
function statusColor(s: ProcessRunStatus): 'primary' | 'success' | 'error' | 'warning' {
  switch (s) {
    case 'Running':
      return 'primary'
    case 'Success':
      return 'success'
    case 'Abandoned':
      return 'warning'
    default:
      return 'error'
  }
}
function statusIcon(s: ProcessRunStatus): string {
  switch (s) {
    case 'Running':
      return 'i-lucide-loader-circle'
    case 'Success':
      return 'i-lucide-circle-check'
    case 'Abandoned':
      return 'i-lucide-circle-slash'
    default:
      return 'i-lucide-circle-x'
  }
}
// The badge passes its icon through the `leadingIcon` slot; spin it only while
// `Running` so the loader-circle actually animates (other statuses stay static).
function statusBadgeUi(s: ProcessRunStatus): { leadingIcon: string } | undefined {
  return s === 'Running' ? { leadingIcon: 'animate-spin' } : undefined
}

// --- Pipeline chain + iterations ---------------------------------------------
// Recent iterations (one full pass of the chain each), newest first, with their
// per-process runs. Paged independently of the runs table below.
const iterationsPage = ref(1)
const iterationsPageSize = 8
// `finishedOnly` makes the API exclude the in-flight pass from BOTH the page and
// the total, so this list is purely completed history and its pagination stays
// consistent (the running pass is shown by the pipeline chain above, fetched
// separately). Filtering client-side instead would desync `total` from the rows.
const iterationsFilters = computed(() => ({
  page: iterationsPage.value,
  pageSize: iterationsPageSize,
  finishedOnly: true,
}))
const {
  data: iterationsData,
  pending: iterationsPending,
  error: iterationsError,
} = useProcessIterations(iterationsFilters)

const finishedIterations = computed<ProcessIteration[]>(() => iterationsData.value?.iterations ?? [])
const iterationsTotal = computed(() => iterationsData.value?.total ?? 0)
const iterationsServerPage = computed(() => iterationsData.value?.page ?? iterationsPage.value)

// The top "current chain" block must always reflect the globally most-recent
// iteration (page 1, item 0), independent of which page of the iterations LIST
// the operator is viewing. A dedicated `page=1&pageSize=1` fetch keeps it both
// correct AND live — paginating the list below never moves it.
const { data: latestIterationData, error: latestIterationError } = useProcessIterations({
  page: 1,
  pageSize: 1,
})
const latestIteration = computed<ProcessIteration | null>(
  () => latestIterationData.value?.iterations?.[0] ?? null,
)

// Per-process outcome within one iteration. `notRun` covers both a process that
// hasn't started yet in the current pass and one that was skipped this pass.
type ChainOutcome = ProcessRunStatus | 'notRun'

interface ChainLink {
  processName: string
  outcome: ChainOutcome
  run: ProcessRun | null
}

// Build the ordered chain for one iteration: the union of the canonical
// PIPELINE_CHAIN order and every process actually present in `runs`. Canonical
// processes keep their known position (annotated `notRun` when absent this
// pass); any extra/unknown process that ran is appended in run order, so no run
// is ever silently dropped just because its name isn't in PIPELINE_CHAIN.
function buildChain(runs: ProcessRun[]): ChainLink[] {
  const byName = new Map(runs.map(run => [run.processName, run]))
  const canonical = new Set(PIPELINE_CHAIN)
  const links: ChainLink[] = PIPELINE_CHAIN.map((processName) => {
    const run = byName.get(processName) ?? null
    return {
      processName,
      outcome: run?.status ?? 'notRun',
      run,
    }
  })
  const seenExtra = new Set<string>()
  for (const run of runs) {
    if (canonical.has(run.processName) || seenExtra.has(run.processName))
      continue
    seenExtra.add(run.processName)
    // Resolve through `byName` like the canonical links do, so a process that
    // ran twice this pass (a retry) shows its latest run in both paths; the
    // loop order still fixes the extra's position at its first appearance.
    const latest = byName.get(run.processName) ?? run
    links.push({ processName: latest.processName, outcome: latest.status, run: latest })
  }
  return links
}

// The live chain shown at the top: the globally newest iteration's outcomes when
// present, otherwise the bare canonical chain with everything not-yet-run. This
// is what highlights "where we currently are". Driven by the dedicated
// latest-iteration fetch so list pagination never changes it.
const currentChain = computed<ChainLink[]>(() => {
  return buildChain(latestIteration.value?.runs ?? [])
})
const currentIterationRunning = computed(() => latestIteration.value?.isRunning ?? false)

// Precompute each finished iteration's chain once, so the recent-iterations
// template shares one array between its v-for and its separator length check
// (which now tracks the actual rendered link count, not PIPELINE_CHAIN.length).
const iterationChains = computed<Map<string, ChainLink[]>>(
  () => new Map(finishedIterations.value.map(it => [it.iterationId, buildChain(it.runs)])),
)

function outcomeColor(outcome: ChainOutcome): 'primary' | 'success' | 'error' | 'warning' | 'neutral' {
  switch (outcome) {
    case 'Running':
      return 'primary'
    case 'Success':
      return 'success'
    case 'Failed':
      return 'error'
    case 'Abandoned':
      return 'warning'
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
    case 'Abandoned':
      return 'i-lucide-circle-slash'
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

// --- Summary formatting ------------------------------------------------------
// Process summaries are free-form JSON (flat scalar maps, nested objects, or
// arrays of per-platform rows). `ProcessSummaryView` renders any of those shapes
// as readable fields/tables; here we only decide whether a summary has content
// to show and keep the raw JSON available as a collapsible fallback.
function hasSummary(summary: ProcessRun['summary']): boolean {
  if (summary === null || summary === undefined) {
    return false
  }
  if (Array.isArray(summary)) {
    return summary.length > 0
  }
  if (typeof summary === 'object') {
    return Object.keys(summary).length > 0
  }
  // Scalar summary (unusual but tolerated): content unless it's an empty string,
  // so an empty payload shows "No summary recorded" rather than a blank field.
  return summary !== ''
}

function summaryJson(summary: ProcessRun['summary']): string | null {
  if (summary === null || summary === undefined) {
    return null
  }
  return JSON.stringify(summary, null, 2)
}

// --- Run slide-over ----------------------------------------------------------
const detailOpen = ref(false)
const selectedRun = ref<ProcessRun | null>(null)
function openDetail(run: ProcessRun) {
  selectedRun.value = run
  detailOpen.value = true
}
const selectedSummaryHasContent = computed(() => hasSummary(selectedRun.value?.summary ?? null))
const selectedSummaryJson = computed(() => summaryJson(selectedRun.value?.summary ?? null))

// --- Iteration slide-over ----------------------------------------------------
// Clicking a (finished) iteration opens a formatted breakdown: one entry per
// process that ran, with its outcome, duration, summary fields and any error —
// rather than the raw per-run JSON.
const iterationDetailOpen = ref(false)
const selectedIteration = ref<ProcessIteration | null>(null)
function openIterationDetail(iteration: ProcessIteration) {
  selectedIteration.value = iteration
  iterationDetailOpen.value = true
}

// The iteration's runs in canonical chain order, keeping only processes that
// actually ran this pass (skip `notRun` placeholders in the detail view).
const selectedIterationLinks = computed<ChainLink[]>(() =>
  selectedIteration.value
    ? buildChain(selectedIteration.value.runs).filter(link => link.run)
    : [],
)

// Precompute each entry's summary state once per iteration, so the template
// doesn't recompute hasSummary/summaryJson twice per row on every render. The
// run's `summary` is handed straight to `ProcessSummaryView` for rendering.
interface IterationEntry {
  link: ChainLink
  hasSummary: boolean
  json: string | null
}
const selectedIterationEntries = computed<IterationEntry[]>(() =>
  selectedIterationLinks.value.map(link => ({
    link,
    hasSummary: hasSummary(link.run?.summary ?? null),
    json: summaryJson(link.run?.summary ?? null),
  })),
)

// Per-iteration outcome tallies for the detail header. Only finished iterations
// reach this view (the in-flight one is excluded from the list), so there is no
// `running` bucket — every run has settled to Success/Failed/Abandoned.
const selectedIterationTally = computed(() => {
  const runs = selectedIteration.value?.runs ?? []
  return {
    success: runs.filter(run => run.status === 'Success').length,
    failed: runs.filter(run => run.status === 'Failed').length,
    abandoned: runs.filter(run => run.status === 'Abandoned').length,
  }
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
          v-if="latestIterationError"
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
                'border-warning/40 bg-warning/10': link.outcome === 'Abandoned',
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
                  'text-warning': link.outcome === 'Abandoned',
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

        <div
          v-if="iterationsError"
          class="py-8 text-center text-sm text-muted border border-default rounded-lg"
        >
          Failed to load recent iterations.
        </div>
        <div v-else-if="iterationsPending" class="space-y-3">
          <USkeleton v-for="i in 3" :key="i" class="h-16 w-full rounded-lg" />
        </div>
        <div
          v-else-if="finishedIterations.length === 0"
          class="py-8 text-center text-sm text-muted border border-default rounded-lg"
        >
          No finished iterations yet.
        </div>
        <div v-else class="space-y-3">
          <button
            v-for="iteration in finishedIterations"
            :key="iteration.iterationId"
            type="button"
            class="block w-full text-left rounded-lg border border-default p-4 bg-elevated/25 transition-colors hover:bg-elevated/50 hover:border-primary/40"
            title="View iteration summary"
            @click="openIterationDetail(iteration)"
          >
            <div class="flex items-center justify-between gap-2 mb-3">
              <span class="text-sm text-muted">
                {{ formatDateTime(iteration.startedAtUtc) }}
              </span>
              <UIcon name="i-lucide-chevron-right" class="size-4 text-dimmed shrink-0" />
            </div>

            <div class="flex flex-wrap items-center gap-y-2">
              <template
                v-for="(link, i) in iterationChains.get(iteration.iterationId)"
                :key="link.processName"
              >
                <span
                  class="inline-flex items-center gap-1.5 rounded-md border px-2 py-1"
                  :class="{
                    'border-primary/50 bg-primary/10': link.outcome === 'Running',
                    'border-success/30 bg-success/5': link.outcome === 'Success',
                    'border-error/40 bg-error/10': link.outcome === 'Failed',
                    'border-warning/40 bg-warning/10': link.outcome === 'Abandoned',
                    'border-default bg-default opacity-50': link.outcome === 'notRun',
                  }"
                  :title="`${link.processName} · ${outcomeLabel(link.outcome)}`"
                >
                  <UIcon
                    :name="outcomeIcon(link.outcome)"
                    class="size-3.5 shrink-0"
                    :class="{
                      'text-primary animate-spin': link.outcome === 'Running',
                      'text-success': link.outcome === 'Success',
                      'text-error': link.outcome === 'Failed',
                      'text-warning': link.outcome === 'Abandoned',
                      'text-dimmed': link.outcome === 'notRun',
                    }"
                  />
                  <span
                    class="text-[11px] font-medium whitespace-nowrap"
                    :class="link.outcome === 'notRun' ? 'text-dimmed' : 'text-default'"
                  >
                    {{ chainLabel(link.processName) }}
                  </span>
                </span>
                <UIcon
                  v-if="i < (iterationChains.get(iteration.iterationId)?.length ?? 0) - 1"
                  name="i-lucide-chevron-right"
                  class="size-3.5 text-dimmed shrink-0 mx-0.5"
                />
              </template>
            </div>
          </button>
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
                :ui="statusBadgeUi(proc.lastStatus)"
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
                    :ui="statusBadgeUi(selectedRun.status)"
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
              <ProcessSummaryView
                v-if="selectedSummaryHasContent"
                :value="selectedRun.summary"
              />
              <p v-else class="text-sm text-dimmed">
                No summary recorded for this run.
              </p>

              <!-- Raw payload stays available for exact values / debugging. -->
              <details v-if="selectedSummaryHasContent && selectedSummaryJson" class="mt-2">
                <summary class="text-xs text-muted cursor-pointer hover:text-default">
                  Raw JSON
                </summary>
                <pre class="mt-1.5 text-xs bg-elevated/50 border border-default rounded-md p-3 overflow-auto">{{ selectedSummaryJson }}</pre>
              </details>
            </div>
          </div>
        </template>
      </USlideover>

      <!-- Iteration detail slide-over: a formatted per-process breakdown of one
           finished pass (outcome, duration, summary fields, error) instead of
           raw JSON. -->
      <USlideover
        v-model:open="iterationDetailOpen"
        :title="selectedIteration ? 'Iteration summary' : 'Iteration'"
        :description="selectedIteration ? formatDateTime(selectedIteration.startedAtUtc) : ''"
      >
        <template #body>
          <div v-if="selectedIteration" class="space-y-5">
            <!-- Outcome tally -->
            <div class="flex flex-wrap gap-2">
              <UBadge
                color="success"
                variant="subtle"
                size="sm"
                :label="`${selectedIterationTally.success} ok`"
              />
              <UBadge
                v-if="selectedIterationTally.failed > 0"
                color="error"
                variant="subtle"
                size="sm"
                :label="`${selectedIterationTally.failed} failed`"
              />
              <UBadge
                v-if="selectedIterationTally.abandoned > 0"
                color="warning"
                variant="subtle"
                size="sm"
                :label="`${selectedIterationTally.abandoned} abandoned`"
              />
            </div>

            <p
              v-if="selectedIterationLinks.length === 0"
              class="text-sm text-dimmed"
            >
              No processes ran in this iteration.
            </p>

            <!-- One collapsible entry per process that ran -->
            <details
              v-for="entry in selectedIterationEntries"
              :key="entry.link.processName"
              class="rounded-lg border border-default bg-elevated/25 overflow-hidden"
            >
              <summary class="flex items-center justify-between gap-2 px-3 py-2.5 cursor-pointer hover:bg-elevated/40">
                <span class="flex items-center gap-2 min-w-0">
                  <UIcon
                    :name="outcomeIcon(entry.link.outcome)"
                    class="size-4 shrink-0"
                    :class="{
                      'text-primary': entry.link.outcome === 'Running',
                      'text-success': entry.link.outcome === 'Success',
                      'text-error': entry.link.outcome === 'Failed',
                      'text-warning': entry.link.outcome === 'Abandoned',
                    }"
                  />
                  <span class="text-sm font-medium text-highlighted truncate">
                    {{ chainLabel(entry.link.processName) }}
                  </span>
                </span>
                <span class="flex items-center gap-2 shrink-0">
                  <span class="text-xs text-dimmed tabular-nums">
                    {{ entry.link.run && entry.link.outcome !== 'Running' ? formatDuration(entry.link.run.durationMs) : '—' }}
                  </span>
                  <UBadge
                    v-if="entry.link.run"
                    :color="statusColor(entry.link.run.status)"
                    variant="subtle"
                    size="sm"
                    :label="entry.link.run.status"
                  />
                </span>
              </summary>

              <div v-if="entry.link.run" class="px-3 pb-3 pt-1 space-y-3 border-t border-default">
                <div v-if="entry.link.run.error">
                  <p class="text-muted text-xs uppercase mb-1.5">
                    Error
                  </p>
                  <pre class="text-xs text-error bg-error/5 border border-error/20 rounded-md p-2.5 overflow-auto whitespace-pre-wrap">{{ entry.link.run.error }}</pre>
                </div>

                <ProcessSummaryView
                  v-if="entry.hasSummary"
                  :value="entry.link.run?.summary"
                />
                <p v-else-if="!entry.link.run.error" class="text-sm text-dimmed">
                  No summary recorded.
                </p>

                <!-- Raw payload stays available for exact values / debugging. -->
                <details v-if="entry.hasSummary && entry.json" class="mt-1">
                  <summary class="text-xs text-muted cursor-pointer hover:text-default">
                    Raw JSON
                  </summary>
                  <pre class="mt-1.5 text-xs bg-elevated/50 border border-default rounded-md p-3 overflow-auto">{{ entry.json }}</pre>
                </details>
              </div>
            </details>
          </div>
        </template>
      </USlideover>
    </template>
  </UDashboardPanel>
</template>
