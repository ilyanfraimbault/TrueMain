<script setup lang="ts">
// One budget family of the Riot quota view (#1458): the regional hosts or the platform
// hosts. Riot keeps one app budget per routing host, so each host is its own row —
// adding two hosts' figures would describe a pool that does not exist. A row expands
// into the caller × endpoint pairs spending that host's budget.
import type { RiotRouteQuota } from '~~/shared/types/ops'
import { formatDateTime, formatNumber, formatPercent, formatPercentOrDash } from '~~/shared/utils/format'

defineProps<{
  title: string
  info: string
  routes: RiotRouteQuota[]
}>()

const expanded = ref<Set<string>>(new Set())
function toggle(route: string) {
  const next = new Set(expanded.value)
  if (next.has(route)) {
    next.delete(route)
  }
  else {
    next.add(route)
  }
  expanded.value = next
}

function fillColor(ratio: number | null): 'primary' | 'warning' | 'error' | 'neutral' {
  if (ratio === null) {
    return 'neutral'
  }
  if (ratio >= 0.9) {
    return 'error'
  }
  return ratio >= 0.7 ? 'warning' : 'primary'
}

function limitLabel(route: RiotRouteQuota): string {
  const binding = route.bindingLimit
  return binding ? `${formatNumber(binding.limit)} / ${binding.windowSeconds}s` : 'no limit header'
}
</script>

<template>
  <UCard :ui="{ body: 'p-0 sm:p-0' }">
    <template #header>
      <PanelTitle :title="title" :info="info" />
    </template>

    <div v-if="routes.length === 0" class="px-4 py-8 text-center text-sm text-muted">
      No calls to these hosts in this window.
    </div>
    <div v-else class="divide-y divide-default">
      <div class="hidden md:grid grid-cols-[7rem_1fr_5rem_6rem_6rem_6rem_2rem] gap-4 px-4 py-2 text-xs text-muted uppercase">
        <span>Host</span>
        <span>Utilisation (window avg)</span>
        <span class="text-right">Now</span>
        <span class="text-right">Calls/min</span>
        <span class="text-right">429 rate</span>
        <span class="text-right">Active min</span>
        <span />
      </div>
      <div v-for="route in routes" :key="route.route">
        <button
          type="button"
          class="w-full grid grid-cols-2 md:grid-cols-[7rem_1fr_5rem_6rem_6rem_6rem_2rem] gap-x-4 gap-y-1 px-4 py-3 text-left text-sm items-center hover:bg-elevated/50"
          :aria-expanded="expanded.has(route.route)"
          @click="toggle(route.route)"
        >
          <span class="font-mono text-highlighted">{{ route.route }}</span>
          <div class="col-span-2 md:col-span-1 order-last md:order-none">
            <div class="flex items-center justify-between text-xs text-muted tabular-nums">
              <span>{{ limitLabel(route) }}</span>
              <span class="text-highlighted">{{ formatPercentOrDash(route.utilisation, 1) }}</span>
            </div>
            <UProgress
              class="mt-1"
              :model-value="Math.min(route.utilisation ?? 0, 1) * 100"
              :max="100"
              :color="fillColor(route.utilisation)"
              size="sm"
            />
          </div>
          <span
            class="text-right tabular-nums"
            :title="route.observedAtUtc ? `${route.appRateLimitCount} at ${formatDateTime(route.observedAtUtc)}` : undefined"
          >
            {{ formatPercentOrDash(route.currentUtilisation, 0) }}
          </span>
          <span class="text-right tabular-nums text-muted">{{ route.callsPerMinute.toFixed(1) }}</span>
          <span
            class="text-right tabular-nums"
            :class="route.rateLimited > 0 ? 'text-warning' : 'text-muted'"
          >
            {{ formatPercentOrDash(route.rateLimitedRate, 2) }}
          </span>
          <span class="text-right tabular-nums text-muted">{{ formatPercent(route.activeMinuteShare, 0) }}</span>
          <UIcon
            :name="expanded.has(route.route) ? 'i-lucide-chevron-up' : 'i-lucide-chevron-down'"
            class="hidden md:block justify-self-end text-muted"
          />
        </button>

        <table v-if="expanded.has(route.route)" class="w-full text-sm mb-2">
          <thead class="text-xs text-muted uppercase">
            <tr>
              <th class="px-4 py-1 text-left font-normal">
                Caller
              </th>
              <th class="px-4 py-1 text-left font-normal">
                Endpoint
              </th>
              <th class="px-4 py-1 text-right font-normal">
                Calls
              </th>
              <th class="px-4 py-1 text-right font-normal">
                Share
              </th>
              <th class="px-4 py-1 text-right font-normal">
                429
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="consumer in route.consumers" :key="`${consumer.caller}|${consumer.endpoint}`">
              <td class="px-4 py-1 text-highlighted">
                {{ consumer.caller }}
              </td>
              <td class="px-4 py-1 font-mono text-muted">
                {{ consumer.endpoint }}
              </td>
              <td class="px-4 py-1 text-right tabular-nums">
                {{ formatNumber(consumer.calls) }}
              </td>
              <td class="px-4 py-1 text-right tabular-nums text-muted">
                {{ formatPercent(consumer.share, 1) }}
              </td>
              <td
                class="px-4 py-1 text-right tabular-nums"
                :class="consumer.rateLimited > 0 ? 'text-warning' : 'text-muted'"
              >
                {{ formatNumber(consumer.rateLimited) }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </UCard>
</template>
