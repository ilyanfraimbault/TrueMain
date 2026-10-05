<script setup lang="ts">
// Budget headroom card of the Riot API tab (#1035): arithmetic on measured cost per
// account, always over the last 7 days regardless of the tab's window — see
// RiotApiUsageQueryService. Split out of ProcessesRiotApi.vue.
import type { RiotApiHeadroom } from '~~/shared/types/ops'
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
const bindingLimitLabel = computed(() => {
  const binding = props.headroom?.bindingLimit
  if (!binding) {
    return null
  }
  return `${formatNumber(binding.limit)} calls / ${formatWindowSeconds(binding.windowSeconds)} → ${formatCallsPerDay(binding.maxCallsPerDay)}`
})
</script>

<template>
  <UCard>
    <template #header>
      <PanelTitle variant="label" title="Budget headroom" />
    </template>

    <div v-if="headroom?.sufficientData" class="flex flex-col gap-4">
      <div>
        <p class="text-xs text-muted uppercase">
          More accounts fit
        </p>
        <p class="mt-1 text-2xl font-semibold text-highlighted tabular-nums">
          ≈ {{ formatNumber(headroom.additionalAccountsHeadroom ?? 0) }}
        </p>
      </div>
      <dl class="grid grid-cols-2 gap-3 text-sm">
        <div>
          <dt class="text-muted">
            Tracked accounts
          </dt>
          <dd class="tabular-nums text-highlighted">
            {{ formatNumber(headroom.trackedAccounts) }}
          </dd>
        </div>
        <div>
          <dt class="text-muted">
            Cost per account
          </dt>
          <dd class="tabular-nums text-highlighted">
            {{ formatCallsPerDay(headroom.callsPerAccountPerDay) }}
          </dd>
        </div>
        <div>
          <dt class="text-muted">
            Binding limit
          </dt>
          <dd class="tabular-nums text-highlighted">
            {{ bindingLimitLabel }}
          </dd>
        </div>
        <div>
          <dt class="text-muted">
            Spare capacity
          </dt>
          <dd class="tabular-nums text-highlighted">
            {{ formatCallsPerDay(headroom.spareCallsPerDay) }}
          </dd>
        </div>
      </dl>
    </div>
    <p v-else class="text-sm text-muted">
      Not enough data yet to estimate: {{ (headroom?.observedWindowHours ?? 0).toFixed(1) }}h observed,
      {{ (headroom?.requiredWindowHours ?? 24).toFixed(0) }}h needed.
      <template v-if="headroom && headroom.observedWindowHours >= headroom.requiredWindowHours">
        <template v-if="headroom.trackedAccounts === 0">
          No accounts are tracked yet.
        </template>
        <template v-else>
          No rate-limit snapshot has been seen yet.
        </template>
      </template>
    </p>
  </UCard>
</template>
