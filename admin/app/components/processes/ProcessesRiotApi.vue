<script setup lang="ts">
// Riot API tab of the Processes page (#1410), formerly the standalone
// `/riot-api` page — Riot usage is a pipeline signal, not a destination of its
// own. Quota per routing host and lane duty cycle from `GET /api/ops/riot-quota`
// (#1458), then call counts per endpoint, status codes and call volume from
// `GET /api/ops/riot-usage`, over one relative window. Both read the per-minute
// `riot_api_call_rollups` the Ingestor writes via its HTTP metrics handler.
//
// This component owns both fetches and the filters; the cards below it
// (`ProcessesRiot*`) only draw what they are handed.
import type {
  RiotCallerUsage,
  RiotEndpointUsage,
  RiotStatusCount,
  RiotUsageWindow,
} from '~~/shared/types/ops'
import { formatDateTime, formatNumber } from '~~/shared/utils/format'

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
const timeSeries = computed(() => usage.value?.timeSeries ?? [])
const headroom = computed(() => usage.value?.headroom ?? null)

// The endpoint table's sort lives here, not in its card: the card unmounts while a
// new window loads.
const sorting = ref([{ id: 'calls', desc: true }])

// The page's single navbar refresh button drives whichever tab is open; it re-reads both.
const anyPending = computed(() => pending.value || quotaPending.value)
defineExpose({
  refresh: () => Promise.all([refresh(), refreshQuota()]),
  pending: anyPending,
})
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
      <ProcessesRiotStatusCodes :status-codes="statusCodes" />
      <ProcessesRiotApiHeadroom :headroom="headroom" />
    </div>

    <!-- Call volume over time -->
    <ProcessesRiotCallVolume :time-series="timeSeries" :usage-window="selectedWindow" :pending="pending" />

    <!-- Consumption by caller -->
    <ProcessesRiotCallers :caller-breakdown="callerBreakdown" :pending="pending" />

    <!-- Endpoint breakdown -->
    <ProcessesRiotEndpoints v-model:sorting="sorting" :endpoints="endpoints" :pending="pending" />
  </template>
</template>
