<script setup lang="ts">
// Pipeline chain of the Processes page: one branch per lane, each at its own
// newest iteration, with the running step highlighted. Two lanes run concurrently
// since #1362, so a flat chain would draw the idle lane's steps as phantom
// "Not run" chips; a `Full` deployment lights up both branches of the same pass.
// Split out of `pages/processes.vue` (#1436); the page owns the fetch so its live
// refresh keeps driving it.
import type { ProcessRun } from '~~/shared/types/ops'
import type { CurrentLane } from '~~/shared/utils/pipeline-lanes'
import { formatDateTime, formatElapsed } from '~~/shared/utils/format'

defineProps<{
  lanes: CurrentLane[]
  running: boolean
  error: unknown
}>()

defineEmits<{
  openRun: [run: ProcessRun]
}>()
</script>

<template>
  <div class="mb-6">
    <div class="flex items-center justify-between gap-2 mb-3">
      <PanelTitle
        variant="label"
        title="Pipeline chain"
        info="One branch per lane, each at its own newest iteration, with the
          running step highlighted. Two lanes run concurrently, so a Full
          deployment lights up both branches of the same pass."
      />
      <span
        v-if="running"
        class="inline-flex items-center gap-1.5 text-xs text-primary font-medium"
      >
        <UIcon name="i-lucide-loader-circle" class="size-3.5 animate-spin" />
        In progress
      </span>
    </div>

    <div
      v-if="error"
      class="py-6 text-center text-sm text-muted border border-default rounded-lg"
    >
      Failed to load the pipeline chain.
    </div>
    <div
      v-else
      class="rounded-lg border border-default bg-elevated/25 p-4 space-y-4"
    >
      <div
        v-for="lane in lanes"
        :key="lane.branch.id"
        class="relative border-l-2 pl-4"
        :class="laneRailClass(lane.branch.outcome)"
      >
        <div class="flex flex-wrap items-center gap-x-2 gap-y-1 mb-2">
          <UTooltip :text="lane.branch.description" :ui="PROCESS_TOOLTIP_UI">
            <span class="inline-flex items-center gap-1.5">
              <UIcon
                :name="outcomeIcon(lane.branch.outcome)"
                class="size-4 shrink-0"
                :class="outcomeTextClass(lane.branch.outcome)"
              />
              <span class="text-sm font-medium text-highlighted">{{ lane.branch.label }}</span>
            </span>
          </UTooltip>
          <span v-if="lane.branch.startedAtUtc" class="text-xs text-dimmed">
            {{ formatDateTime(lane.branch.startedAtUtc) }}
          </span>
          <span v-if="lane.branch.durationMs !== null" class="text-xs text-dimmed tabular-nums">
            · {{ formatElapsed(lane.branch.durationMs) }}
          </span>
          <span
            v-if="lane.isRunning"
            class="inline-flex items-center gap-1 text-xs text-primary font-medium"
          >
            <UIcon name="i-lucide-loader-circle" class="size-3 animate-spin" />
            In progress
          </span>
        </div>

        <div class="flex flex-wrap items-center gap-y-2">
          <template v-for="(link, i) in lane.branch.links" :key="link.processName">
            <UTooltip :ui="PROCESS_TOOLTIP_UI">
              <button
                type="button"
                class="group inline-flex items-center gap-2 rounded-md border px-2.5 py-1.5 transition-colors"
                :class="{
                  'border-primary/50 bg-primary/10': link.outcome === 'Running',
                  'border-success/30 bg-success/5': link.outcome === 'Success',
                  'border-error/40 bg-error/10': link.outcome === 'Failed',
                  'border-warning/40 bg-warning/10': link.outcome === 'Abandoned',
                  'border-default bg-default opacity-60': link.outcome === 'notRun',
                  'cursor-default': !link.run,
                }"
                :disabled="!link.run"
                @click="link.run && $emit('openRun', link.run)"
              >
                <UIcon
                  :name="outcomeIcon(link.outcome)"
                  class="size-4 shrink-0"
                  :class="outcomeTextClass(link.outcome)"
                />
                <span
                  class="text-xs font-medium whitespace-nowrap"
                  :class="link.outcome === 'notRun' ? 'text-dimmed' : 'text-highlighted'"
                >
                  {{ chainLabel(link.processName) }}
                </span>
              </button>
              <template #content>
                <ProcessTooltipContent :process-name="link.processName" :context="chainTooltipContext(link)" />
              </template>
            </UTooltip>
            <UIcon
              v-if="i < lane.branch.links.length - 1"
              name="i-lucide-chevron-right"
              class="size-4 text-dimmed shrink-0 mx-0.5"
            />
          </template>
        </div>
      </div>
    </div>
  </div>
</template>
