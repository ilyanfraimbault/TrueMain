<script setup lang="ts">
// The steps of one finished iteration as a waterfall: each step on its own row, its
// bar placed where it ran inside the pass and as wide as it took. Opened from a row of
// the recent-iterations list; a step that ran opens its run detail.
import type { ProcessIteration, ProcessRun } from '~~/shared/types/ops'
import type { ChainLink, LaneBranch } from '~~/shared/utils/pipeline-lanes'
import { formatClock, formatElapsed } from '~~/shared/utils/format'
import { iterationSpan, placeRun } from '~~/shared/utils/iteration-timeline'
import { buildLaneBranches } from '~~/shared/utils/pipeline-lanes'

const props = defineProps<{
  iteration: ProcessIteration
}>()

defineEmits<{
  openRun: [run: ProcessRun]
}>()

const branches = computed<LaneBranch[]>(() => buildLaneBranches(props.iteration.runs))
const span = computed(() => iterationSpan(props.iteration))

// What a row without a bar says instead. A skip is the cadence guard deciding the
// step was not due; a step with no run at all in a finished pass never started.
function idleNote(link: ChainLink): string {
  return link.outcome === 'Skipped' ? 'Skipped · not due yet' : 'Not run this pass'
}

function hasBar(link: ChainLink): link is ChainLink & { run: ProcessRun } {
  return link.run !== null && link.outcome !== 'Skipped'
}
</script>

<template>
  <div class="space-y-3">
    <div v-for="branch in branches" :key="branch.id">
      <p v-if="branches.length > 1" class="text-xs font-medium text-muted mb-1">
        {{ branch.label }}
      </p>

      <component
        :is="link.run ? 'button' : 'div'"
        v-for="link in branch.links"
        :key="link.processName"
        :type="link.run ? 'button' : undefined"
        class="grid w-full grid-cols-[1rem_minmax(7rem,11rem)_3rem_1fr_4.5rem] items-center gap-x-3 rounded px-1.5 py-1 text-left text-xs"
        :class="link.run ? 'hover:bg-elevated/50 cursor-pointer' : ''"
        :title="link.run ? 'View run details' : undefined"
        @click="link.run && $emit('openRun', link.run)"
      >
        <UIcon
          :name="outcomeIcon(link.outcome)"
          class="size-3.5"
          :class="outcomeTextClass(link.outcome)"
        />
        <UTooltip :ui="PROCESS_TOOLTIP_UI">
          <span
            class="truncate"
            :class="hasBar(link) ? 'text-highlighted' : 'text-dimmed'"
          >
            {{ chainLabel(link.processName) }}
          </span>
          <template #content>
            <ProcessTooltipContent :process-name="link.processName" :context="iterationChipContext(link)" />
          </template>
        </UTooltip>
        <span class="tabular-nums text-dimmed">
          {{ hasBar(link) ? formatClock(link.run.startedAtUtc) : '' }}
        </span>
        <span v-if="hasBar(link)" class="relative h-2">
          <span
            class="absolute inset-y-0 rounded-sm"
            :class="outcomeBarClass(link.outcome)"
            :style="{
              left: `${placeRun(link.run, span).offsetPct}%`,
              width: `${placeRun(link.run, span).widthPct}%`,
            }"
          />
        </span>
        <span v-else class="text-dimmed italic">{{ idleNote(link) }}</span>
        <span class="text-right tabular-nums" :class="hasBar(link) ? 'text-default' : 'text-dimmed'">
          {{ hasBar(link) ? formatElapsed(link.run.durationMs) : '' }}
        </span>
      </component>
    </div>
  </div>
</template>
