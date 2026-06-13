<script setup lang="ts">
// Riot API usage panel (#93) — call counts per endpoint, status-code breakdown,
// rate-limit status and a call-volume time-series from `GET /api/ops/riot-usage`,
// over a relative window. Metrics are sourced from the per-call `riot_api_calls`
// Mongo collection the Ingestor writes via its HTTP metrics handler.
import type { TableColumn } from '@nuxt/ui'
import type {
  RiotEndpointUsage,
  RiotStatusCount,
  RiotUsageWindow,
} from '~~/shared/types/ops'
import { formatDateTime, formatDuration, formatNumber } from '~~/shared/utils/format'

const WINDOW_ITEMS: { label: string, value: RiotUsageWindow }[] = [
  { label: 'Last hour', value: '1h' },
  { label: 'Last 24h', value: '24h' },
  { label: 'Last 7 days', value: '7d' },
]

const window = ref<RiotUsageWindow>('24h')
const endpoint = ref('')

const filters = computed(() => ({
  window: window.value,
  endpoint: endpoint.value.trim() || undefined,
}))

const { data, pending, error, refresh } = useRiotUsage(filters)

const endpoints = computed<RiotEndpointUsage[]>(() => data.value?.endpoints ?? [])
const statusCodes = computed<RiotStatusCount[]>(() => data.value?.statusCodes ?? [])
const totalCalls = computed(() => data.value?.totalCalls ?? 0)
const totalErrors = computed(() => data.value?.totalErrors ?? 0)
const errorRate = computed(() => data.value?.errorRate ?? 0)
const avgLatencyMs = computed(() => data.value?.avgLatencyMs ?? 0)
const rateLimit = computed(() => data.value?.rateLimit ?? null)

// --- Summary -----------------------------------------------------------------
const errorRatePct = computed(() => `${(errorRate.value * 100).toFixed(1)}%`)

// --- Status-code breakdown ---------------------------------------------------
// Coloured chips rather than a chart: the 200/429/5xx split reads best as a
// labelled, colour-coded list. Status 0 is a transport fault (no response).
function statusColor(code: number): 'success' | 'warning' | 'error' | 'neutral' {
  if (code === 0 || code >= 500) {
    return 'error'
  }
  if (code === 429) {
    return 'warning'
  }
  if (code >= 400) {
    return 'warning'
  }
  if (code >= 200 && code < 400) {
    return 'success'
  }
  return 'neutral'
}
function statusLabel(code: number): string {
  return code === 0 ? 'failed' : String(code)
}
const statusTotal = computed(() =>
  statusCodes.value.reduce((sum, s) => sum + s.count, 0),
)
function statusPct(count: number): string {
  const total = statusTotal.value
  return total > 0 ? `${((count / total) * 100).toFixed(1)}%` : '0%'
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
    if (Number.isFinite(value) && Number.isFinite(win)) {
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
const appRateBuckets = computed(() =>
  buildRateBuckets(rateLimit.value?.appRateLimit, rateLimit.value?.appRateLimitCount),
)
function formatWindowSeconds(seconds: number): string {
  if (seconds % 3600 === 0) {
    return `${seconds / 3600}h`
  }
  if (seconds % 60 === 0) {
    return `${seconds / 60}m`
  }
  return `${seconds}s`
}
function rateColor(bucket: RateBucket): 'primary' | 'warning' | 'error' {
  const ratio = bucket.limit > 0 ? bucket.count / bucket.limit : 0
  if (ratio >= 0.9) {
    return 'error'
  }
  if (ratio >= 0.7) {
    return 'warning'
  }
  return 'primary'
}

// --- Time-series chart -------------------------------------------------------
// Call volume per bucket as an AREA chart (mirrors the "Matches over time"
// pattern). Bucket size is fixed by the window (5m / 1h / 6h) server-side.
const timeSeriesData = computed(() =>
  (data.value?.timeSeries ?? []).map(bucket => ({
    label: formatBucketLabel(bucket.bucketUtc),
    calls: bucket.calls,
  })),
)
const timeSeriesCategories = { calls: { name: 'Calls', color: '#34d399' } }
const timeSeriesXFormatter = computed(() =>
  indexLabelFormatter(timeSeriesData.value, row => row.label),
)
function formatBucketLabel(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) {
    return iso
  }
  if (window.value === '7d') {
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

// --- Endpoint table ----------------------------------------------------------
const sorting = ref([{ id: 'calls', desc: true }])
const columns: TableColumn<RiotEndpointUsage>[] = [
  { accessorKey: 'endpoint', header: ({ column }) => sortableHeader(column, 'Endpoint') },
  { accessorKey: 'calls', header: ({ column }) => sortableHeader(column, 'Calls', 'right') },
  { accessorKey: 'successes', header: ({ column }) => sortableHeader(column, 'Success', 'right') },
  { accessorKey: 'errors', header: ({ column }) => sortableHeader(column, 'Errors', 'right') },
  { accessorKey: 'avgLatencyMs', header: ({ column }) => sortableHeader(column, 'Avg latency', 'right') },
  { accessorKey: 'lastCalledAtUtc', header: ({ column }) => sortableHeader(column, 'Last call', 'right') },
]
</script>

<template>
  <UDashboardPanel id="riot-api">
    <template #header>
      <UDashboardNavbar title="Riot API" icon="i-lucide-gauge">
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
            v-model="endpoint"
            icon="i-lucide-search"
            placeholder="Filter endpoint…"
            class="w-56"
          />
          <USelect
            v-model="window"
            :items="WINDOW_ITEMS"
            class="w-40"
          />
        </template>
        <template #right>
          <UBadge
            v-if="!pending"
            color="neutral"
            variant="subtle"
            :label="`${formatNumber(totalCalls)} calls`"
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
        title="Failed to load Riot API usage"
        :description="error.message"
        class="mb-6"
      />

      <!-- Summary tiles -->
      <div class="grid grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
        <UCard>
          <p class="text-xs text-muted uppercase">
            Total calls
          </p>
          <p class="mt-1 text-2xl font-semibold text-highlighted tabular-nums">
            {{ formatNumber(totalCalls) }}
          </p>
        </UCard>
        <UCard>
          <p class="text-xs text-muted uppercase">
            Error rate
          </p>
          <p
            class="mt-1 text-2xl font-semibold tabular-nums"
            :class="errorRate > 0 ? 'text-error' : 'text-highlighted'"
          >
            {{ errorRatePct }}
          </p>
          <p class="text-xs text-muted tabular-nums">
            {{ formatNumber(totalErrors) }} errors
          </p>
        </UCard>
        <UCard>
          <p class="text-xs text-muted uppercase">
            Avg latency
          </p>
          <p class="mt-1 text-2xl font-semibold text-highlighted tabular-nums">
            {{ formatDuration(avgLatencyMs) }}
          </p>
        </UCard>
        <UCard>
          <p class="text-xs text-muted uppercase">
            Endpoints used
          </p>
          <p class="mt-1 text-2xl font-semibold text-highlighted tabular-nums">
            {{ formatNumber(endpoints.length) }}
          </p>
        </UCard>
      </div>

      <!-- Rate limit + status codes -->
      <div class="grid gap-6 lg:grid-cols-2 mb-6">
        <UCard>
          <template #header>
            <div class="flex items-center justify-between gap-2">
              <p class="text-xs text-muted uppercase">
                App rate limit
              </p>
              <UBadge
                v-if="rateLimit?.observedAtUtc"
                color="neutral"
                variant="subtle"
                :label="formatDateTime(rateLimit.observedAtUtc)"
              />
            </div>
          </template>

          <div v-if="appRateBuckets.length" class="flex flex-col gap-4">
            <div v-for="bucket in appRateBuckets" :key="bucket.windowSeconds">
              <div class="flex items-center justify-between text-sm">
                <span class="text-muted">per {{ formatWindowSeconds(bucket.windowSeconds) }}</span>
                <span class="tabular-nums text-highlighted">
                  {{ formatNumber(bucket.count) }} / {{ formatNumber(bucket.limit) }}
                </span>
              </div>
              <UProgress
                class="mt-1"
                :model-value="bucket.count"
                :max="bucket.limit || 1"
                :color="rateColor(bucket)"
                size="sm"
              />
            </div>
          </div>
          <p v-else class="text-sm text-muted">
            No rate-limit headers seen in this window.
          </p>
        </UCard>

        <UCard>
          <template #header>
            <p class="text-xs text-muted uppercase">
              Status codes
            </p>
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
                  class="h-full rounded-full bg-primary"
                  :style="{ width: statusPct(status.count) }"
                />
              </div>
              <span class="tabular-nums text-highlighted w-16 text-right">
                {{ formatNumber(status.count) }}
              </span>
              <span class="tabular-nums text-muted w-14 text-right">
                {{ statusPct(status.count) }}
              </span>
            </div>
          </div>
          <p v-else class="text-sm text-muted">
            No calls recorded in this window.
          </p>
        </UCard>
      </div>

      <!-- Call volume over time -->
      <UCard class="mb-6" :ui="{ root: 'overflow-visible' }">
        <template #header>
          <p class="text-xs text-muted uppercase">
            Call volume over time
          </p>
        </template>
        <USkeleton v-if="pending" class="h-[260px] w-full" />
        <div
          v-else-if="timeSeriesData.length === 0"
          class="flex h-[260px] items-center justify-center text-sm text-muted"
        >
          No calls recorded in this window.
        </div>
        <ClientOnly v-else>
          <NcAreaChart
            :data="timeSeriesData"
            :height="260"
            :categories="timeSeriesCategories"
            :x-num-ticks="Math.min(timeSeriesData.length, 8)"
            :x-formatter="timeSeriesXFormatter"
            :y-formatter="formatCount"
            v-bind="areaChartProps()"
          />
          <template #fallback>
            <USkeleton class="h-[260px] w-full" />
          </template>
        </ClientOnly>
      </UCard>

      <!-- Endpoint breakdown -->
      <UCard :ui="{ body: 'p-0 sm:p-0' }">
        <template #header>
          <div class="flex items-center justify-between gap-2">
            <p class="text-sm font-medium text-highlighted">
              Endpoints
            </p>
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
              {{ formatDuration(row.original.avgLatencyMs) }}
            </div>
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
  </UDashboardPanel>
</template>
