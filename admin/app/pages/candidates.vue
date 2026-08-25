<script setup lang="ts">
// Candidates panel — two read-only diagnostic views over the ingestion pipeline:
//   1. Candidates (`main_candidates`): New → Scored → Queued → Processing →
//      Validated (or Rejected). Searchable by Riot ID / PUUID / champion id,
//      filterable by status + region, server-paged. Row click opens a slide-over
//      with the exact pipeline stage, timestamps, ingested match count and the
//      linked manual seed request (if one brought the account in).
//   2. Demandes d'ajout (`seed_requests`): the manual "add a main" intake list,
//      Pending → Resolving → Ingested / Failed, with the Riot ID, region,
//      requested/processed times and any error. Filterable by status + search.
// Read-only — no write actions in v1 (use "Add mains" to queue a request).
import type { TableColumn } from '@nuxt/ui'
import type {
  CandidateDetail,
  CandidateRow,
  IngestionTimeGranularity,
  MainCandidateStatus,
  SeedRequestReadModel,
  SeedRequestStatus,
} from '~~/shared/types/ops'
import { formatDateTime, formatDuration, formatNumber } from '~~/shared/utils/format'

const { nameFor, iconFor } = useChampionStatic()

// =============================================================================
// Throughput (#1024) — the historical half of this page
// =============================================================================
// Everything below this block shows the funnel's *instantaneous* state, on which
// a full-but-stalled pipeline and a flowing one look identical. These series
// answer the other question — how much actually moved per period — and read the
// recorded process-run summaries, never `main_candidates` row counts: retention
// prunes stale candidates, so counting rows by status per past period would make
// every old bucket shrink a little more each week.
const funnelGranularityItems: { label: string, value: IngestionTimeGranularity }[] = [
  { label: 'Day', value: 'day' },
  { label: 'Week', value: 'week' },
  { label: 'Month', value: 'month' },
]
const funnelWindowItems = [
  { label: '7 days', value: 7 },
  { label: '30 days', value: 30 },
  { label: '90 days', value: 90 },
]
const funnelGranularity = ref<IngestionTimeGranularity>('day')
const funnelWindowDays = ref(30)

const {
  data: funnel,
  pending: funnelPending,
  error: funnelError,
  refresh: refreshFunnel,
} = useCandidateFunnel(funnelGranularity, funnelWindowDays)

const {
  data: latency,
  pending: latencyPending,
  error: latencyError,
  refresh: refreshLatency,
} = useCandidateQueueLatency()

const funnelBuckets = computed(() => funnel.value?.buckets ?? [])

// All three throughput charts below draw BARS or a CUMULATIVE line, never a line
// through per-period counts (#1218). Intake and progression are flows — how much
// moved during the bucket — and bars are the mark for a flow. The outcome chart is
// the running total, because "how many accounts have we validated" is a roster
// size: a stock, which is what a line is for. Drawn as lines, the flat-looking
// `validated` series read as a dead counter when it was moving ~350 a day.

// Intake is stacked: the three sources add up to "candidates that entered", and
// the split matters because they fail independently — the ladder drying up and
// the harvest drying up are different incidents with the same total.
const intakeChartData = computed(() =>
  funnelBuckets.value.map(bucket => ({
    label: formatBucketLabel(bucket.bucket, funnelGranularity.value),
    ladder: bucket.intakeLadder,
    harvest: bucket.intakeHarvest,
    manual: bucket.intakeManual,
  })),
)
const intakeChartCategories = {
  ladder: { name: 'Ladder', color: CHART_SERIES[0] },
  harvest: { name: 'Harvest', color: CHART_SERIES[1] },
  manual: { name: 'Manual seed', color: CHART_SERIES[2] },
}

// Progression carries the competitive cut and nothing else: scored vs promoted,
// GROUPED bars rather than stacked, because promoted is a subset of scored and
// stacking them would draw a total that counts the same candidate twice.
// `validated` used to be a third series here; it lost that seat to the outcome
// chart below. On a shared linear axis 10.5k validated against 147k scored is
// squashed onto the baseline whatever the mark — the series was unreadable, not
// the chart type. That split also keeps the palette rule in `charts.ts`: three
// colours, and a fourth series gets its own chart.
const progressChartData = computed(() =>
  funnelBuckets.value.map(bucket => ({
    label: formatBucketLabel(bucket.bucket, funnelGranularity.value),
    scored: bucket.scored,
    promoted: bucket.promoted,
  })),
)
const progressChartCategories = {
  scored: { name: 'Scored', color: CHART_SERIES[0] },
  promoted: { name: 'Promoted', color: CHART_SERIES[1] },
}

// Outcome: the two ends of the funnel as RUNNING TOTALS over the window — accounts
// that cleared ingestion, and accounts demoted back out. A total is a stock, so
// this one is genuinely a line, and the pair is readable together because both
// live in the same order of magnitude once scored/promoted are off the axis.
//
// The accumulation restarts at the left edge of the selected window, so switching
// 7/30/90 days rescales the curve; the caption under the chart says so rather than
// letting a reader take the endpoint for an all-time roster count.
const outcomeChartData = computed(() => {
  const buckets = funnelBuckets.value
  // null (counter did not exist yet) must not accumulate as zero — the curve has
  // to start where measurement started (#924). `undefined` breaks the line there.
  const validated = runningTotal(buckets.map(bucket => bucket.validated))
  const demoted = runningTotal(buckets.map(bucket => bucket.demoted))
  return buckets.map((bucket, index) => ({
    label: formatBucketLabel(bucket.bucket, funnelGranularity.value),
    validated: validated[index] ?? undefined,
    demoted: demoted[index] ?? undefined,
  }))
})
const outcomeChartCategories = {
  validated: { name: 'Validated (cumulative)', color: CHART_SERIES[0] },
  demoted: { name: 'Demoted (cumulative)', color: CHART_SERIES[1] },
}

const intakeXFormatter = computed(() =>
  indexLabelFormatter(intakeChartData.value, row => row.label),
)
const progressXFormatter = computed(() =>
  indexLabelFormatter(progressChartData.value, row => row.label),
)
const outcomeXFormatter = computed(() =>
  indexLabelFormatter(outcomeChartData.value, row => row.label),
)

// Window totals, rendered as text under each chart. Not decoration: the series
// colours sit below 3:1 against the light surface, so the numbers rather than the
// fills are what carries magnitude for a reader who cannot separate the hues.
const funnelTotals = computed(() =>
  funnelBuckets.value.reduce(
    (acc, bucket) => ({
      ladder: acc.ladder + bucket.intakeLadder,
      harvest: acc.harvest + bucket.intakeHarvest,
      manual: acc.manual + bucket.intakeManual,
      scored: acc.scored + bucket.scored,
      promoted: acc.promoted + bucket.promoted,
      // Stays null until a measured bucket contributes, so an unmeasured window
      // totals to an em dash rather than to a confident zero.
      validated: bucket.validated === null
        ? acc.validated
        : (acc.validated ?? 0) + bucket.validated,
      demoted: acc.demoted + bucket.demoted,
      runs: acc.runs + bucket.runs,
    }),
    {
      ladder: 0,
      harvest: 0,
      manual: 0,
      scored: 0,
      promoted: 0,
      validated: null as number | null,
      demoted: 0,
      runs: 0,
    },
  ),
)

const funnelBoundNote = computed(() => {
  const payload = funnel.value
  if (!payload || payload.buckets.length === 0) {
    return null
  }
  return payload.windowDays > payload.retentionDays
    ? `Run history is kept ${payload.retentionDays} days, so the series stops there rather than at the requested ${payload.windowDays}.`
    : null
})

const validatedNote = computed(() => {
  const payload = funnel.value
  if (!payload || payload.buckets.length === 0) {
    return null
  }
  if (!payload.validatedFirstMeasuredAtUtc) {
    return 'Validated accounts are not counted in any run on record yet — the counter ships with this panel and fills from the next ingestion run onwards.'
  }
  const firstBucket = payload.buckets[0]?.bucket
  // Only worth saying while the window still reaches back past the counter.
  return firstBucket && new Date(firstBucket) < new Date(payload.validatedFirstMeasuredAtUtc)
    ? `Validated was not measured before ${formatDateTime(payload.validatedFirstMeasuredAtUtc)} — that stretch of the line is absent, not zero.`
    : null
})

/** Seconds → the shared duration label; null (no sample) reads as an em dash. */
function latencyLabel(seconds: number | null | undefined): string {
  return seconds === null || seconds === undefined ? '—' : formatDuration(seconds * 1000)
}

// =============================================================================
// View 1 — Candidates
// =============================================================================
// Status badge colors/icons live in `utils/candidate-status.ts` (auto-imported)
// so this page and the account explorer badge a status identically.
const candidateStatusItems = [
  { label: 'All statuses', value: ALL },
  ...CANDIDATE_STATUSES.map(status => ({ label: status, value: status })),
]

const candidateStatus = ref<'all' | MainCandidateStatus>(ALL)
const candidateRegion = ref<string>(ALL)
const candidateSearch = ref('')
// Debounce the search so we don't fire a request per keystroke.
const candidateSearchDebounced = refDebounced(candidateSearch, 300)
const candidatePage = ref(1)
const candidatePageSize = 25

// Reset to page 1 whenever a filter narrows/widens the result set.
watch([candidateStatus, candidateRegion, candidateSearchDebounced], () => {
  candidatePage.value = 1
})

const candidateFilters = computed(() => ({
  status: candidateStatus.value === ALL ? undefined : candidateStatus.value,
  region: candidateRegion.value === ALL ? undefined : candidateRegion.value,
  search: candidateSearchDebounced.value.trim() || undefined,
  page: candidatePage.value,
  pageSize: candidatePageSize,
}))

const hasCandidateFilters = computed(() =>
  candidateStatus.value !== ALL
  || candidateRegion.value !== ALL
  || Boolean(candidateSearch.value.trim()),
)
function resetCandidateFilters() {
  candidateStatus.value = ALL
  candidateRegion.value = ALL
  candidateSearch.value = ''
}

const {
  data: candidateData,
  pending: candidatePending,
  error: candidateError,
  refresh: refreshCandidates,
} = useCandidates(candidateFilters)

const candidateRows = computed<CandidateRow[]>(() => candidateData.value?.candidates ?? [])
const candidateTotal = computed(() => candidateData.value?.total ?? 0)

// Riot ID display: "gameName#tagLine" when resolved, else an em dash.
function riotIdLabel(gameName: string | null, tagLine: string | null): string {
  if (!gameName) {
    return '—'
  }
  return tagLine ? `${gameName}#${tagLine}` : gameName
}

const candidateColumns: TableColumn<CandidateRow>[] = [
  { accessorKey: 'championId', header: 'Champion' },
  { accessorKey: 'riotId', header: 'Riot ID' },
  { accessorKey: 'platformId', header: 'Region' },
  { accessorKey: 'status', header: 'Status' },
  { accessorKey: 'score', header: 'Score' },
  { accessorKey: 'discoveredAtUtc', header: 'Discovered' },
  { accessorKey: 'validatedAtUtc', header: 'Validated' },
]

// =============================================================================
// View 2 — Demandes d'ajout (seed requests)
// =============================================================================
// Badge colors/icons live in `utils/pipeline-status.ts` (auto-imported) so this
// page and the account explorer badge a status identically.
const seedStatusItems = [
  { label: 'All statuses', value: ALL },
  ...SEED_STATUSES.map(status => ({ label: status, value: status })),
]

const seedStatus = ref<'all' | SeedRequestStatus>(ALL)
const seedRegion = ref<string>(ALL)
const seedSearch = ref('')
const seedSearchDebounced = refDebounced(seedSearch, 300)
const seedPage = ref(1)
const seedPageSize = 25

// Reset to page 1 whenever a filter narrows/widens the result set.
watch([seedStatus, seedRegion, seedSearchDebounced], () => {
  seedPage.value = 1
})

const seedFilters = computed(() => ({
  status: seedStatus.value === ALL ? undefined : seedStatus.value,
  region: seedRegion.value === ALL ? undefined : seedRegion.value,
  search: seedSearchDebounced.value.trim() || undefined,
  page: seedPage.value,
  pageSize: seedPageSize,
}))

const hasSeedFilters = computed(() =>
  seedStatus.value !== ALL
  || seedRegion.value !== ALL
  || Boolean(seedSearch.value.trim()),
)
function resetSeedFilters() {
  seedStatus.value = ALL
  seedRegion.value = ALL
  seedSearch.value = ''
}

const {
  data: seedData,
  pending: seedPending,
  error: seedError,
  refresh: refreshSeedRequests,
} = useSeedRequests(seedFilters)

const seedRows = computed<SeedRequestReadModel[]>(() => seedData.value?.requests ?? [])
const seedTotal = computed(() => seedData.value?.total ?? 0)

const seedColumns: TableColumn<SeedRequestReadModel>[] = [
  { accessorKey: 'riotId', header: 'Riot ID' },
  { accessorKey: 'platformId', header: 'Region' },
  { accessorKey: 'status', header: 'Status' },
  { accessorKey: 'requestedAtUtc', header: 'Requested' },
  { accessorKey: 'processedAtUtc', header: 'Processed' },
  { accessorKey: 'error', header: 'Error' },
]

// --- Refresh every panel at once --------------------------------------------
const anyPending = computed(() =>
  candidatePending.value || seedPending.value || funnelPending.value || latencyPending.value,
)
function refreshAll() {
  refreshCandidates()
  refreshSeedRequests()
  refreshFunnel()
  refreshLatency()
}

// =============================================================================
// Candidate detail slide-over (Data Quality pattern: imperative + deep-linkable)
// =============================================================================
const {
  detailOpen,
  detail,
  detailPending,
  detailError,
  detailErrorTraceId,
  openDetail,
} = useDeepLinkedDetail<CandidateDetail>({
  queryKey: 'candidate',
  fetch: getCandidateDetail,
  notFoundMessage: id => `No candidate found with id "${id}".`,
  loadErrorMessage: 'Failed to load candidate detail.',
})

const detailTitle = computed(() =>
  detail.value
    ? riotIdLabel(detail.value.gameName, detail.value.tagLine)
    : 'Candidate detail',
)

// Ordered pipeline stages for the detail stepper, with the timestamp that marks
// each one. Processing has no dedicated timestamp on the entity, so it inherits
// the discovered floor for ordering only.
const PIPELINE_ORDER: MainCandidateStatus[] = [
  'New',
  'Scored',
  'Queued',
  'Processing',
  'Validated',
]
function isRejected(status: MainCandidateStatus | undefined): boolean {
  return status === 'Rejected'
}
</script>

<template>
  <UDashboardPanel id="candidates">
    <template #header>
      <UDashboardNavbar title="Candidates" icon="i-lucide-users-round">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            icon="i-lucide-refresh-cw"
            color="neutral"
            variant="ghost"
            :loading="anyPending"
            aria-label="Refresh"
            @click="refreshAll()"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <!-- ========================== Throughput =========================== -->
      <UCard :ui="{ root: 'overflow-visible' }" class="mb-8">
        <template #header>
          <div class="flex items-start justify-between gap-4">
            <div>
              <p class="text-sm font-medium text-highlighted">
                Throughput
              </p>
              <p class="text-xs text-dimmed mt-0.5">
                How much moved, by <em>run</em> date — bars are per period, the outcome
                curve is the running total. The list below shows the funnel's current
                state, which looks the same whether it is flowing or stalled.
              </p>
            </div>
            <div class="flex items-center gap-2">
              <USelect
                v-model="funnelGranularity"
                :items="funnelGranularityItems"
                class="w-28"
                aria-label="Funnel bucket granularity"
              />
              <USelect
                v-model="funnelWindowDays"
                :items="funnelWindowItems"
                class="w-28"
                aria-label="Funnel window"
              />
            </div>
          </div>
        </template>

        <FetchErrorAlert
          v-if="funnelError"
          :error="funnelError"
          title="Failed to load candidate throughput"
        />
        <USkeleton v-else-if="funnelPending" class="h-[260px] w-full" />
        <div
          v-else-if="funnelBuckets.length === 0"
          class="h-[260px] flex items-center justify-center text-sm text-muted"
        >
          No pipeline runs on record in this window.
        </div>
        <template v-else>
          <div class="grid gap-6 lg:grid-cols-2">
            <div>
              <p class="text-xs text-muted uppercase mb-1.5">
                Intake by source, per period
              </p>
              <ClientOnly>
                <ChartsBarChart
                  :data="intakeChartData"
                  :height="240"
                  :categories="intakeChartCategories"
                  :y-axis="['ladder', 'harvest', 'manual']"
                  :stacked="true"
                  :x-num-ticks="Math.min(intakeChartData.length, 6)"
                  :x-formatter="intakeXFormatter"
                  :y-formatter="formatCount"
                  :tooltip-title-formatter="labelTooltipTitle"
                  v-bind="multiTimeBarProps()"
                />
                <template #fallback>
                  <USkeleton class="h-[240px] w-full" />
                </template>
              </ClientOnly>
              <p class="mt-3 text-xs text-dimmed tabular-nums">
                {{ formatNumber(funnelTotals.ladder) }} ladder ·
                {{ formatNumber(funnelTotals.harvest) }} harvest ·
                {{ formatNumber(funnelTotals.manual) }} manual seed
              </p>
            </div>

            <div>
              <p class="text-xs text-muted uppercase mb-1.5">
                Progression, per period
              </p>
              <ClientOnly>
                <ChartsBarChart
                  :data="progressChartData"
                  :height="240"
                  :categories="progressChartCategories"
                  :y-axis="['scored', 'promoted']"
                  :x-num-ticks="Math.min(progressChartData.length, 6)"
                  :x-formatter="progressXFormatter"
                  :y-formatter="formatCount"
                  :tooltip-title-formatter="labelTooltipTitle"
                  v-bind="multiTimeBarProps()"
                />
                <template #fallback>
                  <USkeleton class="h-[240px] w-full" />
                </template>
              </ClientOnly>
              <p class="mt-3 text-xs text-dimmed tabular-nums">
                {{ formatNumber(funnelTotals.scored) }} scored ·
                {{ formatNumber(funnelTotals.promoted) }} promoted
              </p>
              <p class="mt-1 text-xs text-dimmed">
                Grouped, not stacked — promoted is the top-N cut taken out of scored.
              </p>
            </div>

            <div class="lg:col-span-2">
              <p class="text-xs text-muted uppercase mb-1.5">
                Outcome, cumulative over the window
              </p>
              <ClientOnly>
                <NcAreaChart
                  :data="outcomeChartData"
                  :height="240"
                  :categories="outcomeChartCategories"
                  :hide-area="true"
                  :x-num-ticks="Math.min(outcomeChartData.length, 8)"
                  :x-formatter="outcomeXFormatter"
                  :y-formatter="formatCount"
                  v-bind="multiAreaChartProps()"
                />
                <template #fallback>
                  <USkeleton class="h-[240px] w-full" />
                </template>
              </ClientOnly>
              <p class="mt-3 text-xs text-dimmed tabular-nums">
                {{ formatNumber(funnelTotals.validated) }} validated ·
                {{ formatNumber(funnelTotals.demoted) }} demoted
              </p>
              <p class="mt-1 text-xs text-dimmed">
                Running totals — the curve restarts at the left edge of the selected
                window, so its endpoint is the window's total, not an all-time count.
              </p>
              <p v-if="validatedNote" class="mt-1 text-xs text-dimmed">
                {{ validatedNote }}
              </p>
            </div>
          </div>

          <p class="mt-4 text-xs text-dimmed tabular-nums">
            {{ formatNumber(funnelTotals.runs) }} pipeline runs in this window
          </p>
          <p v-if="funnelBoundNote" class="mt-1 text-xs text-dimmed">
            {{ funnelBoundNote }}
          </p>
        </template>

        <template #footer>
          <FetchErrorAlert
            v-if="latencyError"
            :error="latencyError"
            title="Failed to load queue latency"
          />
          <USkeleton v-else-if="latencyPending" class="h-16 w-full" />
          <div v-else-if="latency">
            <p class="text-xs text-muted uppercase mb-2">
              Queue latency — snapshot
            </p>
            <div class="grid gap-4 sm:grid-cols-2">
              <div>
                <p class="text-xs text-dimmed">
                  Discovered → scored
                </p>
                <p class="text-sm text-highlighted tabular-nums">
                  {{ latencyLabel(latency.discoveredToScored.medianSeconds) }} median ·
                  {{ latencyLabel(latency.discoveredToScored.p90Seconds) }} p90
                  <span class="text-dimmed">
                    ({{ formatNumber(latency.discoveredToScored.samples) }} candidates)
                  </span>
                </p>
              </div>
              <div>
                <p class="text-xs text-dimmed">
                  Scored → validated
                </p>
                <p class="text-sm text-highlighted tabular-nums">
                  {{ latencyLabel(latency.scoredToValidated.medianSeconds) }} median ·
                  {{ latencyLabel(latency.scoredToValidated.p90Seconds) }} p90
                  <span class="text-dimmed">
                    ({{ formatNumber(latency.scoredToValidated.samples) }} candidates)
                  </span>
                </p>
              </div>
            </div>
            <p class="mt-2 text-xs text-dimmed">
              Measured over the {{ formatNumber(latency.retainedCandidates) }} candidates
              retained right now, not over history: pruned candidates are not in it, so
              this says how fast the queue serves what is in it — not how long a
              candidate waits.
            </p>
          </div>
        </template>
      </UCard>

      <!-- ============================ View 1 ============================= -->
      <UCard :ui="{ body: 'p-0 sm:p-0' }" class="mb-8">
        <template #header>
          <div class="flex flex-col gap-3">
            <div class="flex items-center justify-between gap-2">
              <div>
                <p class="text-sm font-medium text-highlighted">
                  Candidates
                </p>
                <p class="text-xs text-dimmed mt-0.5">
                  Main-candidate ingestion pipeline · New → Scored → Queued →
                  Processing → Validated.
                </p>
              </div>
              <UBadge
                v-if="!candidatePending"
                color="neutral"
                variant="subtle"
                :label="`${formatNumber(candidateTotal)} total`"
              />
            </div>

            <!-- Candidate filters -->
            <div class="flex flex-wrap items-center gap-2">
              <UInput
                v-model="candidateSearch"
                icon="i-lucide-search"
                placeholder="Riot ID, PUUID or champion id"
                class="w-full sm:w-80"
                :loading="candidatePending"
              />
              <USelect
                v-model="candidateStatus"
                :items="candidateStatusItems"
                icon="i-lucide-filter"
                placeholder="Status"
                class="w-44"
              />
              <USelect
                v-model="candidateRegion"
                :items="REGION_ITEMS"
                icon="i-lucide-globe"
                placeholder="Region"
                class="w-40"
              />
              <UButton
                v-if="hasCandidateFilters"
                icon="i-lucide-x"
                color="neutral"
                variant="ghost"
                label="Clear"
                @click="resetCandidateFilters"
              />
            </div>
          </div>
        </template>

        <FetchErrorAlert
          v-if="candidateError"
          :error="candidateError"
          title="Failed to load candidates"
          class="m-4"
        />

        <UTable
          :data="candidateRows"
          :columns="candidateColumns"
          :loading="candidatePending"
          loading-color="primary"
          :ui="{ tr: 'cursor-pointer hover:bg-elevated/40', td: 'py-2' }"
          @select="(_event, row) => openDetail(row.original.id)"
        >
          <template #championId-cell="{ row }">
            <div class="flex items-center gap-2.5">
              <NuxtImg
                v-if="iconFor(row.original.championId)"
                :src="iconFor(row.original.championId)!"
                :alt="nameFor(row.original.championId)"
                width="28"
                height="28"
                loading="lazy"
                class="size-7 rounded-md ring-1 ring-default"
              />
              <div
                v-else
                class="size-7 rounded-md bg-elevated ring-1 ring-default"
              />
              <span class="font-medium text-highlighted">
                {{ nameFor(row.original.championId) }}
              </span>
            </div>
          </template>
          <template #riotId-cell="{ row }">
            <span
              class="text-sm"
              :class="row.original.gameName ? 'text-default' : 'text-dimmed italic'"
            >
              {{ riotIdLabel(row.original.gameName, row.original.tagLine) }}
            </span>
          </template>
          <template #platformId-cell="{ row }">
            <span class="font-mono text-xs text-muted">{{ row.original.platformId }}</span>
          </template>
          <template #status-cell="{ row }">
            <UBadge
              :color="candidateStatusColor(row.original.status)"
              variant="subtle"
              size="sm"
              :icon="candidateStatusIcon(row.original.status)"
              :label="row.original.status"
            />
          </template>
          <template #score-cell="{ row }">
            <span class="tabular-nums text-sm">{{ row.original.score.toFixed(2) }}</span>
          </template>
          <template #discoveredAtUtc-cell="{ row }">
            <span class="tabular-nums text-xs text-muted">
              {{ formatDateTime(row.original.discoveredAtUtc) }}
            </span>
          </template>
          <template #validatedAtUtc-cell="{ row }">
            <span class="tabular-nums text-xs text-muted">
              {{ formatDateTime(row.original.validatedAtUtc) }}
            </span>
          </template>

          <template #empty>
            <div class="py-10 text-center text-sm text-muted">
              No candidates match these filters.
            </div>
          </template>
        </UTable>

        <!-- Pager -->
        <div
          v-if="candidateTotal > candidatePageSize"
          class="flex items-center justify-between gap-2 border-t border-default px-4 py-3"
        >
          <p class="text-xs text-muted tabular-nums">
            Page {{ candidatePage.toLocaleString('en-US') }} of
            {{ Math.max(1, Math.ceil(candidateTotal / candidatePageSize)).toLocaleString('en-US') }}
          </p>
          <UPagination
            v-model:page="candidatePage"
            :total="candidateTotal"
            :items-per-page="candidatePageSize"
            :sibling-count="1"
            active-color="primary"
            variant="subtle"
            :disabled="candidatePending"
            show-edges
          />
        </div>
      </UCard>

      <!-- ============================ View 2 ============================= -->
      <UCard :ui="{ body: 'p-0 sm:p-0' }">
        <template #header>
          <div class="flex flex-col gap-3">
            <div class="flex items-center justify-between gap-2">
              <div>
                <p class="text-sm font-medium text-highlighted">
                  Demandes d'ajout
                </p>
                <p class="text-xs text-dimmed mt-0.5">
                  Manual "add a main" requests · Pending → Resolving → Ingested /
                  Failed.
                </p>
              </div>
              <UBadge
                v-if="!seedPending"
                color="neutral"
                variant="subtle"
                :label="`${formatNumber(seedTotal)} total`"
              />
            </div>

            <!-- Seed-request filters -->
            <div class="flex flex-wrap items-center gap-2">
              <UInput
                v-model="seedSearch"
                icon="i-lucide-search"
                placeholder="Search Riot ID (gameName / tagLine)"
                class="w-full sm:w-80"
                :loading="seedPending"
              />
              <USelect
                v-model="seedStatus"
                :items="seedStatusItems"
                icon="i-lucide-filter"
                placeholder="Status"
                class="w-44"
              />
              <USelect
                v-model="seedRegion"
                :items="REGION_ITEMS"
                icon="i-lucide-globe"
                placeholder="Region"
                class="w-40"
              />
              <UButton
                v-if="hasSeedFilters"
                icon="i-lucide-x"
                color="neutral"
                variant="ghost"
                label="Clear"
                @click="resetSeedFilters"
              />
            </div>
          </div>
        </template>

        <FetchErrorAlert
          v-if="seedError"
          :error="seedError"
          title="Failed to load seed requests"
          class="m-4"
        />

        <UTable
          :data="seedRows"
          :columns="seedColumns"
          :loading="seedPending"
          loading-color="primary"
          :ui="{ td: 'py-2' }"
        >
          <template #riotId-cell="{ row }">
            <span class="text-sm text-default">
              {{ riotIdLabel(row.original.gameName, row.original.tagLine) }}
            </span>
          </template>
          <template #platformId-cell="{ row }">
            <span class="font-mono text-xs text-muted">{{ row.original.platformId }}</span>
          </template>
          <template #status-cell="{ row }">
            <UBadge
              :color="seedStatusColor(row.original.status)"
              variant="subtle"
              size="sm"
              :icon="seedStatusIcon(row.original.status)"
              :label="row.original.status"
            />
          </template>
          <template #requestedAtUtc-cell="{ row }">
            <span class="tabular-nums text-xs text-muted">
              {{ formatDateTime(row.original.requestedAtUtc) }}
            </span>
          </template>
          <template #processedAtUtc-cell="{ row }">
            <span class="tabular-nums text-xs text-muted">
              {{ formatDateTime(row.original.processedAtUtc) }}
            </span>
          </template>
          <template #error-cell="{ row }">
            <span
              v-if="row.original.error"
              class="text-xs text-error line-clamp-2"
              :title="row.original.error"
            >
              {{ row.original.error }}
            </span>
            <span v-else class="text-dimmed">—</span>
          </template>

          <template #empty>
            <div class="py-10 text-center text-sm text-muted">
              No add requests match these filters.
            </div>
          </template>
        </UTable>

        <!-- Pager -->
        <div
          v-if="seedTotal > seedPageSize"
          class="flex items-center justify-between gap-2 border-t border-default px-4 py-3"
        >
          <p class="text-xs text-muted tabular-nums">
            Page {{ seedPage.toLocaleString('en-US') }} of
            {{ Math.max(1, Math.ceil(seedTotal / seedPageSize)).toLocaleString('en-US') }}
          </p>
          <UPagination
            v-model:page="seedPage"
            :total="seedTotal"
            :items-per-page="seedPageSize"
            :sibling-count="1"
            active-color="primary"
            variant="subtle"
            :disabled="seedPending"
            show-edges
          />
        </div>
      </UCard>

      <!-- Candidate detail slide-over -->
      <USlideover
        v-model:open="detailOpen"
        :title="detailTitle"
        :ui="{ content: 'sm:max-w-xl' }"
      >
        <template #body>
          <div v-if="detailPending" class="space-y-4">
            <USkeleton class="h-16 w-full" />
            <USkeleton class="h-48 w-full" />
          </div>

          <FetchErrorAlert
            v-else-if="detailError"
            :message="detailError"
            :trace-id="detailErrorTraceId"
            title="Could not load candidate"
          />

          <div v-else-if="detail" class="space-y-6">
            <!-- Identity header -->
            <div class="flex items-center gap-3">
              <NuxtImg
                v-if="iconFor(detail.championId)"
                :src="iconFor(detail.championId)!"
                :alt="nameFor(detail.championId)"
                width="40"
                height="40"
                loading="lazy"
                class="size-10 rounded-lg ring-1 ring-default"
              />
              <div v-else class="size-10 rounded-lg bg-elevated ring-1 ring-default" />
              <div class="min-w-0">
                <p class="text-sm font-medium text-highlighted truncate">
                  {{ nameFor(detail.championId) }}
                </p>
                <p class="text-xs text-muted truncate">
                  {{ riotIdLabel(detail.gameName, detail.tagLine) }}
                </p>
              </div>
              <UBadge
                :color="candidateStatusColor(detail.status)"
                variant="subtle"
                size="sm"
                :icon="candidateStatusIcon(detail.status)"
                :label="detail.status"
                class="ml-auto shrink-0"
              />
            </div>

            <!-- Pipeline stage stepper -->
            <div>
              <p class="text-muted text-xs uppercase mb-2">Pipeline stage</p>
              <UAlert
                v-if="isRejected(detail.status)"
                color="error"
                variant="subtle"
                icon="i-lucide-circle-x"
                title="Rejected"
                description="This candidate was ruled out of the pipeline (not a main)."
              />
              <ol v-else class="flex flex-wrap items-center gap-1.5">
                <li
                  v-for="stage in PIPELINE_ORDER"
                  :key="stage"
                  class="flex items-center gap-1.5"
                >
                  <UBadge
                    :color="PIPELINE_ORDER.indexOf(stage) <= PIPELINE_ORDER.indexOf(detail.status)
                      ? candidateStatusColor(detail.status)
                      : 'neutral'"
                    :variant="PIPELINE_ORDER.indexOf(stage) <= PIPELINE_ORDER.indexOf(detail.status)
                      ? 'subtle'
                      : 'soft'"
                    size="sm"
                    :label="stage"
                  />
                  <UIcon
                    v-if="stage !== 'Validated'"
                    name="i-lucide-chevron-right"
                    class="size-3 text-dimmed"
                  />
                </li>
              </ol>
            </div>

            <!-- Facts -->
            <dl class="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Region</dt>
                <dd class="font-mono text-xs">{{ detail.platformId }}</dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Score</dt>
                <dd class="tabular-nums">{{ detail.score.toFixed(3) }}</dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Mastery points</dt>
                <dd class="tabular-nums">{{ formatNumber(detail.championPoints) }}</dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Mastery rank</dt>
                <dd class="tabular-nums">#{{ detail.championRankInMasteryTop }}</dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Ingested matches</dt>
                <dd class="tabular-nums">{{ formatNumber(detail.ingestedMatchCount) }}</dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Last played</dt>
                <dd class="tabular-nums text-xs">{{ formatDateTime(detail.lastPlayTimeUtc) }}</dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Discovered</dt>
                <dd class="tabular-nums text-xs">{{ formatDateTime(detail.discoveredAtUtc) }}</dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Scored</dt>
                <dd class="tabular-nums text-xs">{{ formatDateTime(detail.scoredAtUtc) }}</dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Validated</dt>
                <dd class="tabular-nums text-xs">{{ formatDateTime(detail.validatedAtUtc) }}</dd>
              </div>
              <div class="col-span-2">
                <dt class="text-muted text-xs uppercase mb-0.5">PUUID</dt>
                <dd class="font-mono text-xs break-all text-muted">{{ detail.puuid }}</dd>
              </div>
            </dl>

            <!-- Linked manual seed request -->
            <div>
              <p class="text-muted text-xs uppercase mb-2">Manual add request</p>
              <div
                v-if="detail.seedRequest"
                class="rounded-lg border border-default p-3 space-y-2"
              >
                <div class="flex items-center justify-between gap-2">
                  <span class="text-sm text-default">
                    {{ riotIdLabel(detail.seedRequest.gameName, detail.seedRequest.tagLine) }}
                  </span>
                  <UBadge
                    :color="seedStatusColor(detail.seedRequest.status)"
                    variant="subtle"
                    size="sm"
                    :icon="seedStatusIcon(detail.seedRequest.status)"
                    :label="detail.seedRequest.status"
                  />
                </div>
                <dl class="grid grid-cols-2 gap-x-4 gap-y-1.5 text-xs">
                  <div>
                    <dt class="text-muted uppercase">Requested</dt>
                    <dd class="tabular-nums">{{ formatDateTime(detail.seedRequest.requestedAtUtc) }}</dd>
                  </div>
                  <div>
                    <dt class="text-muted uppercase">Processed</dt>
                    <dd class="tabular-nums">{{ formatDateTime(detail.seedRequest.processedAtUtc) }}</dd>
                  </div>
                </dl>
                <UAlert
                  v-if="detail.seedRequest.error"
                  color="error"
                  variant="subtle"
                  size="sm"
                  icon="i-lucide-triangle-alert"
                  :description="detail.seedRequest.error"
                />
              </div>
              <p v-else class="text-sm text-muted">
                Discovered organically by the ladder — no manual request.
              </p>
            </div>
          </div>
        </template>
      </USlideover>
    </template>
  </UDashboardPanel>
</template>
