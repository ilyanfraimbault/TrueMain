<script setup lang="ts">
// Riot API tab: the status-code breakdown of the window. Coloured chips rather than
// a chart: the 200/429/5xx split reads best as a labelled, colour-coded list.
// Status 0 is a transport fault (no response).
import type { RiotStatusCount } from '~~/shared/types/ops'
import { formatNumber, formatPercent, formatPercentOrDash } from '~~/shared/utils/format'

const props = defineProps<{ statusCodes: RiotStatusCount[] }>()

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
  props.statusCodes.reduce((sum, s) => sum + s.count, 0),
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
</script>

<template>
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
</template>
