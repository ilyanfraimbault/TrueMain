<script setup lang="ts">
// Throughput card of the Accounts hub's Pipeline tab (#1024): how much moved
// through the `main_candidates` funnel per period, where it stands (the stock
// curves of <AccountsCandidateStock>), and the queue latency snapshot. Split out
// of `AccountsPipeline.vue` (#1436); owns its own fetches and exposes
// `refresh`/`pending` so the hub's navbar button still covers them.
import type { IngestionTimeGranularity } from '~~/shared/types/ops'
import { formatElapsed, formatNumber } from '~~/shared/utils/format'

// =============================================================================
// Throughput (#1024) — the historical half of this page
// =============================================================================
// Everything below this block shows the funnel's *instantaneous* state, on which
// a full-but-stalled pipeline and a flowing one look identical. These series
// answer the other question — how much actually moved per period — and read the
// recorded process-run summaries, never `main_candidates` row counts: retention
// prunes stale candidates, so counting rows by status per past period would make
// every old bucket shrink a little more each week.
// Hour is here for the level curves (#1403), not for the bars: New and Processing are
// transient by construction — scoring drains its whole backlog each run, a claim lasts
// one ingestion pass — so at daily resolution both read 0 forever and can say nothing
// about scoring falling behind or leases going unreaped. The flow series bucket by hour
// just as correctly, they are simply rarely asked to.
const funnelGranularityItems: { label: string, value: IngestionTimeGranularity }[] = [
  { label: 'Hour', value: 'hour' },
  { label: 'Day', value: 'day' },
  { label: 'Week', value: 'week' },
  { label: 'Month', value: 'month' },
]
const funnelWindowItems = [
  { label: '2 days', value: 2 },
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

// The funnel's LEVEL (#1403), drawn by <AccountsCandidateStock> under the flow charts.
// Fetched here so the hub's refresh button and pending flag cover it too.
const {
  data: stock,
  pending: stockPending,
  error: stockError,
  refresh: refreshStock,
} = useCandidateStock(funnelGranularity, funnelWindowDays)

const {
  data: latency,
  pending: latencyPending,
  error: latencyError,
  refresh: refreshLatency,
} = useCandidateQueueLatency()

const funnelBuckets = computed(() => funnel.value?.buckets ?? [])

// All three throughput charts below draw BARS or a CUMULATIVE line, never a line
// through per-period counts (#1218). Intake and progression are flows — how much
// moved during the bucket — and bars are the mark for a flow. The state chart is
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
// `validated` used to be a third series here; it lost that seat to the state
// chart below. On a shared linear axis 10.5k validated against 147k scored is
// squashed onto the baseline whatever the mark — the series was unreadable, not
// the chart type. That split also keeps the palette rule in `chart-palette.ts`,
// which still stands: a fourth series gets its own chart. The six-slot list there
// is not a licence to widen this one — slots 4-6 exist for the state chart below,
// where a single axis was the requirement, and they cost the palette its
// "legible whatever the order" property to do it.
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

const intakeXFormatter = computed(() =>
  indexLabelFormatter(intakeChartData.value, row => row.label),
)
const progressXFormatter = computed(() =>
  indexLabelFormatter(progressChartData.value, row => row.label),
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
      demoted: acc.demoted + bucket.demoted,
      runs: acc.runs + bucket.runs,
    }),
    {
      ladder: 0,
      harvest: 0,
      manual: 0,
      scored: 0,
      promoted: 0,
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

/** Seconds → the shared duration label; null (no sample) reads as an em dash. */
function latencyLabel(seconds: number | null | undefined): string {
  return seconds === null || seconds === undefined ? '—' : formatElapsed(seconds * 1000)
}

function refresh() {
  refreshFunnel()
  refreshStock()
  refreshLatency()
}
const pending = computed(() => funnelPending.value || stockPending.value || latencyPending.value)
defineExpose({ refresh, pending })
</script>

<template>
  <UCard :ui="{ root: 'overflow-visible' }" class="mb-8">
    <template #header>
      <div class="flex items-start justify-between gap-4">
        <PanelTitle title="Throughput" subtitle="How much moved and where the funnel stands, by run date.">
          <template #info>
            <p>
              Bars are per-period flows, the curves below are levels.
            </p>
            <p>
              The list further down shows only the current state, which looks the
              same whether the funnel is flowing or stalled.
            </p>
          </template>
        </PanelTitle>
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
          <PanelTitle
            variant="label"
            title="Intake by source, per period"
            class="mb-1.5"
          />
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
          <p class="mt-3 text-xs text-dimmed tabular-nums">
            {{ formatNumber(funnelTotals.ladder) }} ladder ·
            {{ formatNumber(funnelTotals.harvest) }} harvest ·
            {{ formatNumber(funnelTotals.manual) }} manual seed
          </p>
        </div>

        <div>
          <PanelTitle
            variant="label"
            title="Progression, per period"
            info="Grouped, not stacked — promoted is the top-N cut taken out of scored."
            class="mb-1.5"
          />
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
          <p class="mt-3 text-xs text-dimmed tabular-nums">
            {{ formatNumber(funnelTotals.scored) }} scored ·
            {{ formatNumber(funnelTotals.promoted) }} promoted
          </p>
        </div>

        <AccountsCandidateStock
          class="lg:col-span-2"
          :funnel-buckets="funnelBuckets"
          :granularity="funnelGranularity"
          :stock="stock"
          :pending="stockPending"
          :error="stockError"
          :demoted-total="funnelTotals.demoted"
        />
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
        <PanelTitle variant="label" title="Queue latency — snapshot" class="mb-2">
          <template #info>
            Measured over the {{ formatNumber(latency.retainedCandidates) }}
            candidates retained right now, not over history: pruned candidates are
            not in it, so this says how fast the queue serves what is in it — not
            how long a candidate waits.
          </template>
        </PanelTitle>
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
      </div>
    </template>
  </UCard>
</template>
