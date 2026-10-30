<script setup lang="ts">
// Performance tab of the Processes page (#2307): how long the Api took to answer each ops
// route — the reads behind every admin panel — from `GET /api/ops/request-latency`, over
// 24 h or 7 d. Measured server-side by the Api's middleware and rolled up per minute in
// Mongo (`request_latency_rollups`), so a slow panel shows up here rather than being timed
// by hand. Owns its fetch and exposes `refresh`/`pending` for the page's navbar button,
// like the Riot API and Scrapers tabs.
import type { RequestLatencyWindow } from '~~/shared/types/ops'
import { computed, ref } from 'vue'
import { formatDateTime, formatNumber } from '~~/shared/utils/format'
import { countSlowRoutes, formatLatency } from '~~/shared/utils/request-latency'

const WINDOW_ITEMS: { label: string, value: RequestLatencyWindow }[] = [
  { label: 'Last 24h', value: '24h' },
  { label: 'Last 7 days', value: '7d' },
]

const selectedWindow = ref<RequestLatencyWindow>('24h')
const { data, pending, refreshing, error, refresh } = useRequestLatency(selectedWindow)
// The last good payload for this window when a re-fetch failed (#1426): a failed read must
// not blank the table into a quiet-looking one.
const latency = useLastGoodPayload(data, () => selectedWindow.value)

const slowCount = computed(() =>
  latency.value ? countSlowRoutes(latency.value.routes, latency.value.slowThresholdMs) : 0,
)
// The window started before the first rollup: only part of it was measured.
const measuredSince = computed(() => {
  const oldest = latency.value?.oldestRetainedUtc
  return oldest && latency.value && Date.parse(oldest) > Date.parse(latency.value.sinceUtc) ? oldest : null
})

// `pending` is "nothing to draw yet" (#2319) and gates the skeleton; the navbar spinner
// follows any request in flight.
defineExpose({ refresh, pending: refreshing })
</script>

<template>
  <div class="flex flex-wrap items-center gap-2 mb-6">
    <USelect v-model="selectedWindow" :items="WINDOW_ITEMS" class="w-40" />
    <div class="flex-1" />
    <UBadge
      v-if="latency && slowCount > 0"
      color="error"
      variant="subtle"
      :label="`${slowCount} route${slowCount === 1 ? '' : 's'} with p95 over ${formatLatency(latency.slowThresholdMs)}`"
    />
  </div>

  <FetchErrorAlert
    v-if="error"
    :error="error"
    title="Failed to load ops request latency"
    class="mb-6"
  />

  <USkeleton v-if="!latency && pending" class="h-64" />
  <UCard v-else-if="latency">
    <template #header>
      <PanelTitle
        title="Ops request latency"
        subtitle="How long the Api took to answer each admin read, slowest p95 first"
        info="Measured by the Api from the request reaching it to its answer, per route template, and rolled up per minute in Mongo (request_latency_rollups). p50 and p95 are estimated from a fixed set of duration buckets and never exceed the slowest request measured. Timeouts count requests the client abandoned or that ended cancelled; 5xx counts the server errors among the rest. The admin proxy's own hop is not included."
      />
    </template>

    <p v-if="error" class="mb-4 text-sm text-muted">
      Showing the last successful reading, generated {{ formatDateTime(latency.generatedAtUtc) }}.
    </p>
    <p v-if="latency.droppedSinceStart > 0" class="mb-4 text-sm text-warning">
      {{ formatNumber(latency.droppedSinceStart) }} requests were measured but not persisted since the Api started — the figures below undercount them.
    </p>

    <p v-if="!latency.oldestRetainedUtc" class="text-sm text-muted">
      Nothing measured yet: the Api has written no request-latency rollup.
    </p>
    <template v-else>
      <p v-if="measuredSince" class="mb-4 text-xs text-muted">
        Measured since {{ formatDateTime(measuredSince) }} — the window starts before the first rollup.
      </p>
      <p v-if="latency.routes.length === 0" class="text-sm text-muted">
        No ops route was called in this window.
      </p>
      <ProcessesPerformanceTable v-else :latency="latency" />
    </template>
  </UCard>
</template>
