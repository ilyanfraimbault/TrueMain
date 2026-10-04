<script setup lang="ts">
// Riot API tab of the Processes page (#1410), formerly the standalone
// `/riot-api` page — Riot usage is a pipeline signal, not a destination of its
// own. Quota per routing host and lane duty cycle from `GET /api/ops/riot-quota`
// (#1458), then call counts per endpoint, status codes and call volume from
// `GET /api/ops/riot-usage`, over one relative window. Both read the per-minute
// `riot_api_call_rollups` the Ingestor writes via its HTTP metrics handler.
import type { TableColumn } from '@nuxt/ui'
import type {
  RiotCallerUsage,
  RiotEndpointUsage,
  RiotStatusCount,
  RiotUsageWindow,
} from '~~/shared/types/ops'
import { formatDateTime, formatElapsed, formatNumber, formatPercent, formatPercentOrDash } from '~~/shared/utils/format'

const WINDOW_ITEMS: { label: string, value: RiotUsageWindow }[] = [
  { label: 'Last hour', value: '1h' },
  { label: 'Last 24h', value: '24h' },
  { label: 'Last 7 days', value: '7d' },
  { label: 'Last 30 days', value: '30d' },
]

// `selectedWindow` (not `window`) to avoid shadowing the browser global.
const selectedWindow = ref<RiotUsageWindow>('24h')
// Debounce the free-text endpoint filter so typing doesn't fire a request per
// keystroke (matches the logs/candidates panels).
const endpointInput = ref('')
const endpoint = refDebounced(endpointInput, 300)

const filters = computed(() => ({
  window: selectedWindow.value,
  endpoint: endpoint.value.trim() || undefined,
}))

const { data, pending, error, refresh } = useRiotUsage(filters)
// What the panel draws (#1426): the current payload, or the last good one for these
// same filters when a re-fetch failed (Nuxt resets `data` on error). `null` = nothing
// measured, so the panel renders no figures instead of a fabricated quiet window.
const usage = useLastGoodPayload(data, () => JSON.stringify(filters.value))
// Per-host quota and lane duty cycle (#1458): window-scoped, never endpoint-filtered —
// a host's budget is shared by every endpoint it serves.
const { data: quotaData, pending: quotaPending, error: quotaError, refresh: refreshQuota } = useRiotQuota(selectedWindow)
const quota = useLastGoodPayload(quotaData, () => selectedWindow.value)

const endpoints = computed<RiotEndpointUsage[]>(() => usage.value?.endpoints ?? [])
const statusCodes = computed<RiotStatusCount[]>(() => usage.value?.statusCodes ?? [])
const callerBreakdown = computed<RiotCallerUsage[]>(() => usage.value?.callerBreakdown ?? [])
const headroom = computed(() => usage.value?.headroom ?? null)

// --- Status-code breakdown ---------------------------------------------------
// Coloured chips rather than a chart: the 200/429/5xx split reads best as a
// labelled, colour-coded list. Status 0 is a transport fault (no response).
function statusColor(code: number): 'success' | 'warning' | 'error' | 'neutral' {
  if (code === 0 || code >= 500) {
    return 'error'
  }
  // 4xx (429 included) — a client/limit problem, not a server fault.
  if (code >= 400) {
    return 'warning'
  }
  // Everything left is 2xx/3xx (all >= 400 already handled above).
  if (code >= 200) {
    return 'success'
  }
  return 'neutral'
}

// Full literal class per status so Tailwind's scanner picks them up (a dynamic
// `bg-${...}` string would not be generated). Mirrors the badge colours.
function statusBarClass(code: number): string {
  switch (statusColor(code)) {
    case 'error':
      return 'bg-error'
    case 'warning':
      return 'bg-warning'
    case 'success':
      return 'bg-success'
    default:
      return 'bg-primary'
  }
}
function statusLabel(code: number): string {
  return code === 0 ? 'failed' : String(code)
}
const statusTotal = computed(() =>
  statusCodes.value.reduce((sum, s) => sum + s.count, 0),
)
/**
 * A status code's share of the window, or `null` when the window counted no calls at
 * all. A share needs a denominator: with nothing to divide by there is no share, and
 * the `0%` this used to print was a measured claim — "this status never happened" —
 * that no reading supports. `formatPercentOrDash` turns the null into the portal's
 * "not measured" dash.
 */
function statusShare(count: number): number | null {
  const total = statusTotal.value
  return total > 0 ? count / total : null
}

// The bar is a drawing of the share, not a reading of it, so an absent share is simply
// no width — the number next to it is what carries "not measured".
function statusBarWidth(count: number): string {
  return formatPercent(statusShare(count) ?? 0, 1)
}

// --- Rate limit ---------------------------------------------------------------
// Riot returns app/method limits as `value:windowSeconds` pairs, comma-joined
// (limit `20:1,100:120`, count `3:1,57:120`). Zip them by window so each bucket
// renders as "count / limit per Ns" with a usage bar.
interface RateBucket {
  windowSeconds: number
  count: number
  limit: number
}
function parsePairs(raw: string | null | undefined): Map<number, number> {
  const out = new Map<number, number>()
  if (!raw) {
    return out
  }
  for (const pair of raw.split(',')) {
    const [value, win] = pair.split(':').map(part => Number(part.trim()))
    // Guard win > 0: a malformed header like "20:" yields Number("") === 0, which
    // would otherwise surface as a bogus "per 0s" bucket.
    if (Number.isFinite(value) && Number.isFinite(win) && win! > 0) {
      out.set(win!, value!)
    }
  }
  return out
}
function buildRateBuckets(
  limit: string | null | undefined,
  count: string | null | undefined,
): RateBucket[] {
  const limits = parsePairs(limit)
  const counts = parsePairs(count)
  return [...limits.entries()]
    .map(([windowSeconds, lim]) => ({
      windowSeconds,
      limit: lim,
      count: counts.get(windowSeconds) ?? 0,
    }))
    .sort((a, b) => a.windowSeconds - b.windowSeconds)
}
function formatWindowSeconds(seconds: number): string {
  if (seconds % 3600 === 0) {
    return `${seconds / 3600}h`
  }
  if (seconds % 60 === 0) {
    return `${seconds / 60}m`
  }
  return `${seconds}s`
}
// Takes the raw numbers (not a RateBucket) so it also drives the per-endpoint
// method-limit bars in the table below, not just the app-limit buckets.
function rateColor(count: number, limit: number): 'primary' | 'warning' | 'error' {
  const ratio = limit > 0 ? count / limit : 0
  if (ratio >= 0.9) {
    return 'error'
  }
  if (ratio >= 0.7) {
    return 'warning'
  }
  return 'primary'
}
// A method-limit header is usually a single pair (e.g. "500:60"); reuse
// buildRateBuckets and take the first (and typically only) bucket.
function methodRateBucket(row: RiotEndpointUsage): RateBucket | null {
  return buildRateBuckets(row.methodRateLimit, row.methodRateLimitCount)[0] ?? null
}

// --- Time-series chart -------------------------------------------------------
// Call volume per bucket as GROUPED BARS (#1218): calls-per-bucket is a flow, and
// the window fixes the bucket size server-side (5m / 1h / 6h) to land 12-28
// buckets, so bars stay wide enough to read at every window.
// Retries (429s) are a second series (#1035): budget spent for no data, worth
// seeing distinctly from the total volume rather than buried in "errors". Grouped
// and never stacked — `Retries` is documented as a SUBSET of `Calls`, so a stack
// would draw a total that counts every 429 twice.
const timeSeriesData = computed(() =>
  (usage.value?.timeSeries ?? []).map(bucket => ({
    label: formatCallBucketLabel(bucket.bucketUtc),
    calls: bucket.calls,
    retries: bucket.retries,
  })),
)
const timeSeriesCategories = {
  calls: { name: 'Calls', color: CHART_SERIES[0] },
  retries: { name: 'Retries (429)', color: CHART_SERIES[1] },
}
const timeSeriesXFormatter = computed(() =>
  indexLabelFormatter(timeSeriesData.value, row => row.label),
)
// Named distinctly from the auto-imported `formatBucketLabel` (charts.ts),
// which formats matches-over-time buckets per granularity.
function formatCallBucketLabel(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) {
    return iso
  }
  if (selectedWindow.value === '30d') {
    return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
  }
  if (selectedWindow.value === '7d') {
    return date.toLocaleString('en-US', {
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    })
  }
  return date.toLocaleTimeString('en-US', {
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  })
}

// --- Consumption by caller -----------------------------------------------------
// Horizontal categorical bar chart (#1035): who is spending the budget. Follows
// the same `horizontalBarProps`/`barChartHeight` pattern as the top-tables /
// top-champions charts elsewhere in the admin.
const callerChartData = computed(() =>
  callerBreakdown.value.map(row => ({ label: row.caller, calls: row.calls })),
)
const callerChartHeight = computed(() =>
  barChartHeight(callerChartData.value.length, { min: 120, step: 32 }),
)
const callerCategories = { calls: { name: 'Calls', color: CHART_PRIMARY } }
const callerLabelFormatter = computed(() =>
  indexLabelFormatter(callerChartData.value, row => row.label),
)

// --- Endpoint table ----------------------------------------------------------
const sorting = ref([{ id: 'calls', desc: true }])
// The page's single navbar refresh button drives whichever tab is open; it re-reads both.
const anyPending = computed(() => pending.value || quotaPending.value)
defineExpose({
  refresh: () => Promise.all([refresh(), refreshQuota()]),
  pending: anyPending,
})

const columns: TableColumn<RiotEndpointUsage>[] = [
  { accessorKey: 'endpoint', header: ({ column }) => sortableHeader(column, 'Endpoint') },
  { accessorKey: 'calls', header: ({ column }) => sortableHeader(column, 'Calls', 'right') },
  { accessorKey: 'successes', header: ({ column }) => sortableHeader(column, 'Success', 'right') },
  { accessorKey: 'errors', header: ({ column }) => sortableHeader(column, 'Errors', 'right') },
  { accessorKey: 'avgLatencyMs', header: ({ column }) => sortableHeader(column, 'Avg latency', 'right') },
  { id: 'methodLimit', header: 'Method limit' },
  { accessorKey: 'lastCalledAtUtc', header: ({ column }) => sortableHeader(column, 'Last call', 'right') },
]
</script>

<template>
  <!-- The window/endpoint filters ride in the body rather than in the page's
       `UDashboardToolbar`: the page owns the header and only one tab's filters
       may show at a time (the Crashes tab of `/logs` does the same). -->
  <div class="flex flex-wrap items-center gap-2 mb-6">
    <UInput
      v-model="endpointInput"
      icon="i-lucide-search"
      placeholder="Exact endpoint key…"
      class="w-56"
    />
    <USelect
      v-model="selectedWindow"
      :items="WINDOW_ITEMS"
      class="w-40"
    />
    <div class="flex-1" />
    <UBadge
      v-if="usage && !pending"
      color="neutral"
      variant="subtle"
      :label="`${formatNumber(usage.totalCalls)} calls`"
    />
  </div>

  <FetchErrorAlert
    v-if="error"
    :error="error"
    title="Failed to load Riot API usage"
    class="mb-6"
  />

  <!-- A failed fetch with nothing measured renders the alert alone: no tile or
       empty state may read as "zero calls" (#1426). -->
  <div v-if="!usage && pending" class="grid grid-cols-2 lg:grid-cols-4 gap-4">
    <USkeleton v-for="n in 4" :key="n" class="h-[88px]" />
  </div>
  <template v-else-if="usage">
    <p v-if="error" class="-mt-4 mb-6 text-sm text-muted">
      Showing the last successful reading, generated {{ formatDateTime(usage.generatedAtUtc) }}.
    </p>
    <ProcessesRiotApiSummary :usage="usage" />

    <FetchErrorAlert
      v-if="quotaError"
      :error="quotaError"
      title="Failed to load the Riot quota per host"
      class="mb-6"
    />
    <ProcessesRiotQuota v-if="quota" :quota="quota" />

    <!-- Status codes + budget headroom -->
    <div class="grid gap-6 lg:grid-cols-2 mb-6">
      <UCard>
        <template #header>
          <PanelTitle variant="label" title="Status codes" />
        </template>

        <div v-if="statusCodes.length" class="flex flex-col gap-2">
          <div
            v-for="status in statusCodes"
            :key="status.statusCode"
            class="flex items-center justify-between gap-3 text-sm"
          >
            <UBadge
              :color="statusColor(status.statusCode)"
              variant="subtle"
              :label="statusLabel(status.statusCode)"
            />
            <div class="flex-1 h-1.5 rounded-full bg-elevated overflow-hidden">
              <div
                class="h-full rounded-full"
                :class="statusBarClass(status.statusCode)"
                :style="{ width: statusBarWidth(status.count) }"
              />
            </div>
            <span class="tabular-nums text-highlighted w-16 text-right">
              {{ formatNumber(status.count) }}
            </span>
            <span class="tabular-nums text-muted w-14 text-right">
              {{ formatPercentOrDash(statusShare(status.count), 1) }}
            </span>
          </div>
        </div>
        <p v-else class="text-sm text-muted">
          No calls recorded in this window.
        </p>
      </UCard>

      <ProcessesRiotApiHeadroom :headroom="headroom" />
    </div>

    <!-- Call volume over time -->
    <UCard class="mb-6" :ui="{ root: 'overflow-visible' }">
      <template #header>
        <PanelTitle variant="label" title="Call volume over time" />
      </template>
      <USkeleton v-if="pending" class="h-[260px] w-full" />
      <div
        v-else-if="timeSeriesData.length === 0"
        class="flex h-[260px] items-center justify-center text-sm text-muted"
      >
        No calls recorded in this window.
      </div>
      <ChartsBarChart
        v-else
        :data="timeSeriesData"
        :height="260"
        :categories="timeSeriesCategories"
        :y-axis="['calls', 'retries']"
        :x-num-ticks="Math.min(timeSeriesData.length, 8)"
        :x-formatter="timeSeriesXFormatter"
        :y-formatter="formatCount"
        :tooltip-title-formatter="labelTooltipTitle"
        v-bind="multiTimeBarProps()"
      />
    </UCard>

    <!-- Consumption by caller -->
    <UCard class="mb-6">
      <template #header>
        <PanelTitle variant="label" title="Consumption by caller" />
      </template>

      <USkeleton v-if="pending" :style="{ height: `${callerChartHeight}px` }" class="w-full" />
      <div
        v-else-if="callerChartData.length === 0"
        class="flex h-[120px] items-center justify-center text-sm text-muted"
      >
        No calls recorded in this window.
      </div>
      <ChartsBarChart
        v-else
        :data="callerChartData"
        :height="callerChartHeight"
        :categories="callerCategories"
        :y-axis="['calls']"
        :y-num-ticks="callerChartData.length"
        :x-formatter="formatCount"
        :y-formatter="callerLabelFormatter"
        :tooltip-title-formatter="labelTooltipTitle"
        v-bind="horizontalBarProps(120)"
      />
    </UCard>

    <!-- Endpoint breakdown -->
    <UCard :ui="{ body: 'p-0 sm:p-0' }">
      <template #header>
        <div class="flex items-center justify-between gap-2">
          <PanelTitle title="Endpoints" />
          <UBadge
            v-if="!pending"
            color="neutral"
            variant="subtle"
            :label="`${formatNumber(endpoints.length)} endpoints`"
          />
        </div>
      </template>

      <UTable
        v-model:sorting="sorting"
        :data="endpoints"
        :columns="columns"
        :loading="pending"
        loading-color="primary"
        :ui="{ td: 'py-2' }"
      >
        <template #endpoint-cell="{ row }">
          <span class="font-mono text-sm text-highlighted">
            {{ row.original.endpoint }}
          </span>
        </template>
        <template #calls-cell="{ row }">
          <div class="text-right tabular-nums font-medium text-highlighted">
            {{ formatNumber(row.original.calls) }}
          </div>
        </template>
        <template #successes-cell="{ row }">
          <div class="text-right tabular-nums text-success">
            {{ formatNumber(row.original.successes) }}
          </div>
        </template>
        <template #errors-cell="{ row }">
          <div
            class="text-right tabular-nums"
            :class="row.original.errors > 0 ? 'text-error' : 'text-muted'"
          >
            {{ formatNumber(row.original.errors) }}
          </div>
        </template>
        <template #avgLatencyMs-cell="{ row }">
          <div class="text-right tabular-nums text-muted">
            {{ formatElapsed(row.original.avgLatencyMs) }}
          </div>
        </template>
        <template #methodLimit-cell="{ row }">
          <!-- `[methodRateBucket(...)]` computes it exactly once per row; the single
               iteration then branches on `bucket` instead of recomputing it. -->
          <template v-for="bucket in [methodRateBucket(row.original)]" :key="row.original.endpoint">
            <div v-if="bucket" class="w-32">
              <div class="flex items-center justify-between text-xs text-muted tabular-nums">
                <span>per {{ formatWindowSeconds(bucket.windowSeconds) }}</span>
                <span>{{ formatNumber(bucket.count) }} / {{ formatNumber(bucket.limit) }}</span>
              </div>
              <UProgress
                :model-value="bucket.count"
                :max="bucket.limit || 1"
                :color="rateColor(bucket.count, bucket.limit)"
                size="sm"
              />
            </div>
            <span v-else class="text-sm text-muted">—</span>
          </template>
        </template>
        <template #lastCalledAtUtc-cell="{ row }">
          <div class="text-right text-sm text-muted">
            {{ formatDateTime(row.original.lastCalledAtUtc) }}
          </div>
        </template>

        <template #empty>
          <div class="py-10 text-center text-sm text-muted">
            No Riot API calls recorded in this window.
          </div>
        </template>
      </UTable>
    </UCard>
  </template>
</template>