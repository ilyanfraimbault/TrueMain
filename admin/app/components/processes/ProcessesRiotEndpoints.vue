<script setup lang="ts">
// Riot API tab: the per-endpoint breakdown — calls, successes, errors, latency, the
// method rate limit Riot last reported, and the last call.
import type { TableColumn } from '@nuxt/ui'
import type { RiotEndpointUsage } from '~~/shared/types/ops'
import { formatDateTime, formatElapsed, formatNumber } from '~~/shared/utils/format'

defineProps<{
  endpoints: RiotEndpointUsage[]
  pending: boolean
}>()

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
// Takes the raw numbers (not a RateBucket) so it can drive any count/limit pair.
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

// --- Table --------------------------------------------------------------------
// Owned by the tab: this card unmounts while a new window loads, and the operator's
// sort must survive that.
const sorting = defineModel<{ id: string, desc: boolean }[]>('sorting', { required: true })

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
