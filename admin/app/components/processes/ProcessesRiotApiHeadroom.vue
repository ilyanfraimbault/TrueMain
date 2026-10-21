<script setup lang="ts">
// Budget headroom card of the Riot API tab (#1035): arithmetic on measured cost per
// account, always over the last 7 days regardless of the tab's window — see
// RiotApiUsageQueryService. Per routing host since #1894: Riot meters each host on its
// own, so every host is measured against the limit it advertised and the tightest one
// caps the headline figure; no row adds two hosts' calls. Split out of ProcessesRiotApi.vue.
import type { RiotApiHeadroom, RiotRouteHeadroom } from '~~/shared/types/ops'
import { computed } from 'vue'
import { formatNumber } from '~~/shared/utils/format'

const props = defineProps<{ headroom: RiotApiHeadroom | null }>()

function formatCallsPerDay(value: number | null): string {
  return value === null ? '—' : `${formatNumber(Math.round(value))}/day`
}
function formatWindowSeconds(seconds: number): string {
  if (seconds % 3600 === 0) {
    return `${seconds / 3600}h`
  }
  return seconds % 60 === 0 ? `${seconds / 60}m` : `${seconds}s`
}
function limitLabel(route: RiotRouteHeadroom): string {
  const binding = route.bindingLimit
  if (!binding) {
    return 'no limit header'
  }
  return `${formatNumber(binding.limit)} / ${formatWindowSeconds(binding.windowSeconds)} → ${formatCallsPerDay(binding.maxCallsPerDay)}`
}
function formatAccounts(value: number | null): string {
  return value === null ? '—' : formatNumber(value)
}
const binding = computed(() =>
  props.headroom?.routes.find(route => route.route === props.headroom?.bindingRoute) ?? null,
)
</script>

<template>
  <UCard>
    <template #header>
      <PanelTitle
        variant="label"
        title="Budget headroom"
        info="Per routing host: each host's calls/day over the last 7 days against the app limit that host advertised. The host with the fewest additional accounts binds. New accounts are assumed to cost each host what the current ones do."
      />
    </template>

    <div v-if="headroom?.sufficientData" class="flex flex-col gap-4">
      <div class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p class="text-xs text-muted uppercase">
            More accounts fit
          </p>
          <p class="mt-1 text-2xl font-semibold text-highlighted tabular-nums">
            ≈ {{ formatAccounts(headroom.additionalAccountsHeadroom) }}
          </p>
          <p v-if="binding" class="mt-1 text-xs text-muted">
            bound by <span class="font-mono text-highlighted">{{ binding.route }}</span>
            ({{ binding.kind }}), {{ formatCallsPerDay(binding.spareCallsPerDay) }} spare
          </p>
        </div>
        <p class="text-sm text-muted tabular-nums">
          {{ formatNumber(headroom.trackedAccounts) }} tracked accounts ·
          {{ headroom.observedWindowHours.toFixed(0) }}h measured
        </p>
      </div>

      <table class="w-full text-sm">
        <thead class="text-xs text-muted uppercase">
          <tr>
            <th class="py-1 text-left font-normal">
              Host
            </th>
            <th class="py-1 text-right font-normal">
              Cost / account
            </th>
            <th class="hidden sm:table-cell py-1 text-right font-normal">
              Binding limit
            </th>
            <th class="py-1 text-right font-normal">
              Spare
            </th>
            <th class="py-1 text-right font-normal">
              Accounts fit
            </th>
          </tr>
        </thead>
        <tbody class="tabular-nums">
          <tr
            v-for="route in headroom.routes"
            :key="route.route"
            :class="route.route === headroom.bindingRoute ? 'text-highlighted font-medium' : 'text-muted'"
          >
            <td class="py-1 font-mono">
              {{ route.route }}
            </td>
            <td class="py-1 text-right">
              {{ formatCallsPerDay(route.callsPerAccountPerDay) }}
            </td>
            <td class="hidden sm:table-cell py-1 text-right">
              {{ limitLabel(route) }}
            </td>
            <td class="py-1 text-right">
              {{ formatCallsPerDay(route.spareCallsPerDay) }}
            </td>
            <td class="py-1 text-right">
              {{ formatAccounts(route.additionalAccountsHeadroom) }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <p v-else class="text-sm text-muted">
      Not enough data yet to estimate: {{ (headroom?.observedWindowHours ?? 0).toFixed(1) }}h of per-host history,
      {{ (headroom?.requiredWindowHours ?? 24).toFixed(0) }}h needed.
      <template v-if="headroom && headroom.observedWindowHours >= headroom.requiredWindowHours">
        <template v-if="headroom.trackedAccounts === 0">
          No accounts are tracked yet.
        </template>
        <template v-else>
          No host has returned a rate-limit header yet.
        </template>
      </template>
    </p>
  </UCard>
</template>
