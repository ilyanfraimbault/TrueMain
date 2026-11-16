<script setup lang="ts">
// Process health rollup of the Processes page: one card per process with its last
// status, last run, last success and failures in the window. Split out of
// `pages/processes.vue` (#1436); the page owns the (live-refreshed) fetch.
import type { DetectorStatus, ProcessRunRollupReadModel } from '#site-shared/contract/api'
import { formatDateTime, formatNumber } from '~~/shared/utils/format'
import { processStatusColor, processStatusIcon } from '~~/shared/utils/pipeline-health'

defineProps<{
  rollup: ProcessRunRollupReadModel[]
  pending: boolean
  /** True when no time window is selected — the failure label says "all time". */
  allTime: boolean
}>()

// Failure-cell colouring follows the API's verdict (`health`): red when the latest run
// failed, amber when the window's failure rate is high though it recovered. The raw
// count is always > 0 over a long window, so it would keep the cell permanently red.
const FAILURE_TEXT: Record<DetectorStatus, string> = {
  red: 'text-error',
  amber: 'text-warning',
  green: 'text-default',
  unknown: 'text-default',
}
function failureTextClass(proc: ProcessRunRollupReadModel): string {
  return FAILURE_TEXT[proc.health]
}
function failureRateLabel(proc: ProcessRunRollupReadModel): string {
  if (proc.runCountInWindow === 0) {
    return '—'
  }
  // floor, not round: never show 100% while the window still has a success.
  return `${Math.floor(proc.failureRateInWindow * 100)}% of ${formatNumber(proc.runCountInWindow)}`
}
</script>

<template>
  <div class="mb-6">
    <PanelTitle variant="label" title="Process health" class="mb-3" />

    <div
      v-if="pending && rollup.length === 0"
      class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4"
    >
      <USkeleton v-for="i in 3" :key="i" class="h-28 w-full rounded-lg" />
    </div>
    <div
      v-else-if="rollup.length === 0"
      class="py-10 text-center text-sm text-muted border border-default rounded-lg"
    >
      No process runs recorded yet.
    </div>
    <div
      v-else
      class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4"
    >
      <div
        v-for="proc in rollup"
        :key="proc.processName"
        class="rounded-lg border p-4 bg-elevated/25"
        :class="{
          'border-error/30': proc.lastStatus === 'Failed',
          'border-primary/40': proc.lastStatus === 'Running',
          'border-default': proc.lastStatus !== 'Failed' && proc.lastStatus !== 'Running',
        }"
      >
        <div class="flex items-center justify-between gap-2 mb-3">
          <p class="font-medium text-highlighted truncate">
            {{ proc.processName }}
          </p>
          <UBadge
            :color="processStatusColor(proc.lastStatus)"
            :icon="processStatusIcon(proc.lastStatus)"
            :ui="statusBadgeUi(proc.lastStatus)"
            variant="subtle"
            size="sm"
            :label="proc.lastStatus"
          />
        </div>
        <dl class="space-y-1.5 text-sm">
          <div class="flex justify-between gap-2">
            <dt class="text-muted">
              Last run
            </dt>
            <dd class="text-default text-right">
              {{ formatDateTime(proc.lastRunAtUtc) }}
            </dd>
          </div>
          <div class="flex justify-between gap-2">
            <dt class="text-muted">
              Last success
            </dt>
            <dd class="text-default text-right">
              {{ formatDateTime(proc.lastSuccessAtUtc) }}
            </dd>
          </div>
          <div class="flex justify-between gap-2">
            <dt class="text-muted">
              {{ allTime ? 'Failures (all time)' : 'Failures (window)' }}
            </dt>
            <dd class="text-right">
              <span
                class="tabular-nums font-medium"
                :class="failureTextClass(proc)"
              >
                {{ formatNumber(proc.failureCountInWindow) }}
              </span>
              <span class="block text-xs text-dimmed tabular-nums">
                {{ failureRateLabel(proc) }}
              </span>
            </dd>
          </div>
        </dl>
      </div>
    </div>
  </div>
</template>
