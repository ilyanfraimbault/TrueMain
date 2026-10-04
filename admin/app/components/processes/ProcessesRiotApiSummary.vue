<script setup lang="ts">
// Summary tiles of the Processes page's Riot API tab. Takes the whole payload and
// nothing optional: the parent only mounts it once a reading exists, so no tile
// ever falls back to a zero standing in for "not measured" (#1426).
import type { RiotApiUsage } from '~~/shared/types/ops'
import { formatElapsed, formatNumber, formatPercent } from '~~/shared/utils/format'

const props = defineProps<{ usage: RiotApiUsage }>()

const errorRatePct = computed(() => formatPercent(props.usage.errorRate, 1))
</script>

<template>
  <div class="grid grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
    <UCard>
      <p class="text-xs text-muted uppercase">
        Total calls
      </p>
      <p class="mt-1 text-2xl font-semibold text-highlighted tabular-nums">
        {{ formatNumber(usage.totalCalls) }}
      </p>
    </UCard>
    <UCard>
      <p class="text-xs text-muted uppercase">
        Error rate
      </p>
      <p
        class="mt-1 text-2xl font-semibold tabular-nums"
        :class="usage.errorRate > 0 ? 'text-error' : 'text-highlighted'"
      >
        {{ errorRatePct }}
      </p>
      <p class="text-xs text-muted tabular-nums">
        {{ formatNumber(usage.totalErrors) }} errors
      </p>
    </UCard>
    <UCard>
      <p class="text-xs text-muted uppercase">
        Avg latency
      </p>
      <p class="mt-1 text-2xl font-semibold text-highlighted tabular-nums">
        {{ formatElapsed(usage.avgLatencyMs) }}
      </p>
    </UCard>
    <UCard>
      <p class="text-xs text-muted uppercase">
        Endpoints used
      </p>
      <p class="mt-1 text-2xl font-semibold text-highlighted tabular-nums">
        {{ formatNumber(usage.endpoints.length) }}
      </p>
    </UCard>
  </div>
</template>
