<script setup lang="ts">
// Lane duty cycle (#1458): the share of wall-clock time each ingestor lane had at least
// one process running, from the recorded process runs — the quantity a throughput ramp
// is meant to move. Each process's own share sits under its lane; they do not add up to
// the lane's figure when two of them overlap.
import type { LaneDutyCycle } from '~~/shared/types/ops'
import { formatNumber, formatPercent } from '~~/shared/utils/format'

defineProps<{ lanes: LaneDutyCycle[] }>()

function hours(value: number): string {
  return value >= 10 ? `${Math.round(value)}h` : `${value.toFixed(1)}h`
}
</script>

<template>
  <UCard>
    <template #header>
      <PanelTitle
        title="Lane duty cycle"
        info="Share of the window during which at least one process of the lane was running: the union of its recorded runs, clipped to the window — never their sum, so a lane cannot exceed 100%. A run still marked Running counts until its last heartbeat. Process shares overlap and need not add up to the lane's."
      />
    </template>

    <p v-if="lanes.length === 0" class="text-sm text-muted">
      No process runs recorded in this window.
    </p>
    <div v-else class="grid gap-6 lg:grid-cols-2">
      <div v-for="lane in lanes" :key="lane.lane">
        <div class="flex items-baseline justify-between gap-2">
          <span class="font-medium text-highlighted">{{ lane.lane }}</span>
          <span class="tabular-nums text-highlighted">{{ formatPercent(lane.dutyCycle, 1) }}</span>
        </div>
        <UProgress class="mt-1" :model-value="lane.dutyCycle * 100" :max="100" size="sm" />
        <p class="mt-1 text-xs text-muted tabular-nums">
          {{ hours(lane.busyHours) }} busy · {{ formatNumber(lane.runs) }} runs
        </p>
        <div class="mt-3 flex flex-col gap-1.5">
          <div
            v-for="process in lane.processes"
            :key="process.processName"
            class="grid grid-cols-[minmax(0,10rem)_1fr_3.5rem] items-center gap-3 text-xs"
          >
            <span class="truncate text-muted" :title="`${process.processName} · ${formatNumber(process.runs)} runs`">
              {{ process.processName }}
            </span>
            <div class="h-1.5 rounded-full bg-elevated overflow-hidden">
              <div class="h-full rounded-full bg-primary/70" :style="{ width: formatPercent(process.dutyCycle, 1) }" />
            </div>
            <span class="text-right tabular-nums text-muted">{{ formatPercent(process.dutyCycle, 1) }}</span>
          </div>
        </div>
      </div>
    </div>
  </UCard>
</template>
