<script setup lang="ts">
// One finished iteration as a single line of the recent-iterations list: what kind of
// pass it was, when it ran, a bar of where its time went, and how its steps ended.
// Expands into the step waterfall; the per-run summaries stay in the slide-over.
import type { ProcessIteration, ProcessRun } from '~~/shared/types/ops'
import type { IterationKindId } from '~~/shared/utils/iteration-timeline'
import { formatClock, formatDayLabel, formatElapsed } from '~~/shared/utils/format'
import { iterationKind, iterationSpan, statusCounts, timedRuns } from '~~/shared/utils/iteration-timeline'

const props = defineProps<{
  iteration: ProcessIteration
  /** Passes of the other lane that ran at the same time, among those on the page. */
  parallel: ProcessIteration[]
}>()

const open = defineModel<boolean>('open', { required: true })

defineEmits<{
  openRun: [run: ProcessRun]
  openIteration: [iteration: ProcessIteration]
}>()

const KIND_ICON: Record<IterationKindId, string> = {
  fetch: 'i-lucide-download',
  aggregate: 'i-lucide-layers',
  full: 'i-lucide-workflow',
  single: 'i-lucide-circle-dot',
}

const kind = computed(() => iterationKind(props.iteration))
const span = computed(() => iterationSpan(props.iteration))
const segments = computed(() => timedRuns(props.iteration.runs))
const counts = computed(() => statusCounts(props.iteration.runs))

// Success always shows ("0 ok" is the point when a pass went wrong); the others only
// when they happened, the alarming ones in their colour.
const tallies = computed(() => [
  { key: 'ok', count: counts.value.Success ?? 0, label: 'ok', class: 'text-default', always: true },
  { key: 'skipped', count: counts.value.Skipped ?? 0, label: 'skipped', class: 'text-dimmed' },
  { key: 'failed', count: counts.value.Failed ?? 0, label: 'failed', class: 'text-error' },
  { key: 'abandoned', count: counts.value.Abandoned ?? 0, label: 'abandoned', class: 'text-warning' },
  { key: 'cancelled', count: counts.value.Cancelled ?? 0, label: 'cancelled', class: 'text-muted' },
].filter(tally => tally.always || tally.count > 0))

const failed = computed(() => (counts.value.Failed ?? 0) > 0 || (counts.value.Abandoned ?? 0) > 0)

// Only the passes on this page are known, and a long aggregate pass overlaps fetch
// passes several pages deep: naming or counting the ones found would read as the full
// list. The row says only that the other lane was running, which stays true whatever
// the page holds.
const parallelLabel = computed(() =>
  props.parallel[0] ? `the ${iterationKind(props.parallel[0]).label} lane` : '',
)
</script>

<template>
  <UCollapsible v-model:open="open" class="border-t border-default first:border-t-0" :class="failed ? 'bg-error/5' : ''">
    <button
      type="button"
      class="grid w-full grid-cols-[auto_1fr_auto_1rem] items-center gap-x-4 gap-y-2 px-4 py-2.5 text-left transition-colors hover:bg-elevated/40 md:grid-cols-[7.5rem_10.5rem_minmax(8rem,1fr)_4.5rem_10rem_1rem]"
      :aria-label="`${kind.label} iteration of ${formatDayLabel(iteration.startedAtUtc)} ${formatClock(iteration.startedAtUtc)}`"
    >
      <UBadge
        color="neutral"
        variant="subtle"
        size="sm"
        :icon="KIND_ICON[kind.id]"
        :label="kind.label"
        class="justify-self-start max-w-full truncate"
      />

      <span class="text-xs tabular-nums whitespace-nowrap">
        <span class="text-dimmed">{{ formatDayLabel(iteration.startedAtUtc) }} · </span>
        <span class="text-default">{{ formatClock(iteration.startedAtUtc) }} → {{ formatClock(iteration.lastActivityAtUtc) }}</span>
      </span>

      <!-- Native titles, not UTooltip: a tooltip trigger swallows the click meant for
           the row, and the expanded waterfall is where a step is actually read. -->
      <span class="col-span-full row-start-2 flex h-2 overflow-hidden rounded-full bg-accented/60 md:col-span-1 md:row-start-auto">
        <span
          v-for="run in segments"
          :key="run.id"
          class="h-full basis-0 border-r border-default/80 last:border-r-0"
          :class="outcomeBarClass(run.status)"
          :style="{ flexGrow: run.durationMs }"
          :title="`${chainLabel(run.processName)} · ${formatElapsed(run.durationMs)}`"
        />
      </span>

      <span class="text-xs tabular-nums text-right text-default">
        {{ formatElapsed(span.durationMs) }}
      </span>

      <span class="hidden md:flex gap-2.5 text-xs whitespace-nowrap">
        <span v-for="tally in tallies" :key="tally.key" :class="tally.class">
          <span class="tabular-nums font-medium">{{ tally.count }}</span> {{ tally.label }}
        </span>
      </span>

      <UIcon
        name="i-lucide-chevron-right"
        class="size-4 text-dimmed transition-transform"
        :class="open ? 'rotate-90' : ''"
      />

      <span
        v-if="parallel.length > 0"
        class="col-span-full flex items-center gap-1.5 text-[11px] text-dimmed md:col-start-3"
      >
        <UIcon name="i-lucide-arrow-left-right" class="size-3" />
        Ran alongside {{ parallelLabel }}
      </span>
    </button>

    <template #content>
      <div class="px-4 pb-3 pt-1 md:pl-[9.5rem]">
        <ProcessesIterationSteps :iteration="iteration" @open-run="$emit('openRun', $event)" />
        <div class="mt-2 flex justify-end">
          <UButton
            size="xs"
            color="neutral"
            variant="ghost"
            icon="i-lucide-file-text"
            label="Run summaries"
            @click="$emit('openIteration', iteration)"
          />
        </div>
      </div>
    </template>
  </UCollapsible>
</template>
