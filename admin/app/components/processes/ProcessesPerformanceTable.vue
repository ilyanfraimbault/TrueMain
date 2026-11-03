<script setup lang="ts">
// The routes of the Performance tab (#2307), slowest p95 first as the API orders them. A
// route above the slow threshold is tinted and badged so the epic's (#2300) offenders stand
// out at the top until they are fixed.
import type { RequestLatency } from '~~/shared/types/ops'
import { computed } from 'vue'
import { formatNumber } from '~~/shared/utils/format'
import { formatLatency, isSlowRoute } from '~~/shared/utils/request-latency'

const props = defineProps<{ latency: RequestLatency }>()

const rows = computed(() => props.latency.routes.map(route => ({
  ...route,
  key: `${route.method} ${route.route}`,
  slow: isSlowRoute(route, props.latency.slowThresholdMs),
})))
</script>

<template>
  <div class="overflow-x-auto">
    <table class="w-full text-sm tabular-nums">
      <thead class="text-xs text-muted">
        <tr class="text-left">
          <th class="py-2 pr-3 font-normal">
            Route
          </th>
          <th class="py-2 pr-3 text-right font-normal whitespace-nowrap">
            Requests
          </th>
          <th class="py-2 pr-3 text-right font-normal whitespace-nowrap">
            p50
          </th>
          <th class="py-2 pr-3 text-right font-normal whitespace-nowrap">
            p95
          </th>
          <th class="py-2 pr-3 text-right font-normal whitespace-nowrap">
            Max
          </th>
          <th class="py-2 pr-3 text-right font-normal whitespace-nowrap">
            Timeouts
          </th>
          <th class="py-2 pr-3 text-right font-normal whitespace-nowrap">
            5xx
          </th>
          <th class="py-2 font-normal">
            p95 over the window
          </th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="row in rows"
          :key="row.key"
          class="border-t border-default"
          :class="row.slow ? 'bg-error/5' : undefined"
          :data-slow="row.slow || undefined"
        >
          <td class="py-2 pr-3">
            <div class="flex items-center gap-2">
              <span class="text-xs text-muted">{{ row.method }}</span>
              <span class="font-mono text-highlighted break-all">{{ row.route }}</span>
              <UBadge v-if="row.slow" color="error" variant="subtle" size="sm" label="slow" />
            </div>
          </td>
          <td class="py-2 pr-3 text-right whitespace-nowrap">
            {{ formatNumber(row.count) }}
          </td>
          <td class="py-2 pr-3 text-right whitespace-nowrap">
            {{ formatLatency(row.p50Ms) }}
          </td>
          <td class="py-2 pr-3 text-right whitespace-nowrap" :class="row.slow ? 'text-error font-semibold' : 'text-highlighted'">
            {{ formatLatency(row.p95Ms) }}
          </td>
          <td class="py-2 pr-3 text-right whitespace-nowrap">
            {{ formatLatency(row.maxMs) }}
          </td>
          <td class="py-2 pr-3 text-right whitespace-nowrap" :class="row.timeouts > 0 ? 'text-warning' : 'text-muted'">
            {{ formatNumber(row.timeouts) }}
          </td>
          <td class="py-2 pr-3 text-right whitespace-nowrap" :class="row.serverErrors > 0 ? 'text-error' : 'text-muted'">
            {{ formatNumber(row.serverErrors) }}
          </td>
          <td class="py-2">
            <ProcessesLatencySparkline
              :series="row.series"
              :threshold-ms="latency.slowThresholdMs"
              :slow="row.slow"
            />
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
