<script setup lang="ts">
// The steps of one finished iteration as a UTimeline: one entry per step of the lane,
// in order, with when it started, how long it took and a recap of its summary (the
// reason it skipped or did nothing, else its first counters). Opened from a row of the
// recent-iterations list; an entry that ran opens its run detail.
import type { TimelineItem } from '@nuxt/ui'
import type { ProcessIteration, ProcessRun } from '~~/shared/types/ops'
import type { ChainLink, ChainOutcome } from '~~/shared/utils/pipeline-lanes'
// Explicit imports so the component tests can mount it without Nuxt's auto-imports.
import { computed } from 'vue'
import { chainLabel, outcomeIcon, outcomeLabel } from '~/utils/process-chain'
import { formatClock, formatElapsed } from '~~/shared/utils/format'
import { buildLaneBranches } from '~~/shared/utils/pipeline-lanes'
import { summaryRecap } from '~~/shared/utils/process-summary'

const props = defineProps<{
  iteration: ProcessIteration
}>()

const emit = defineEmits<{
  openRun: [run: ProcessRun]
}>()

type StepItem = TimelineItem & { run: ProcessRun | null, meta: string }

// The indicator carries the outcome, so a column of steps reads at a glance; a step
// that did no work stays visibly quieter than one that did.
const INDICATOR_CLASS: Record<ChainOutcome, string> = {
  Success: 'bg-success/15 text-success',
  Failed: 'bg-error/15 text-error',
  Abandoned: 'bg-warning/15 text-warning',
  Cancelled: 'bg-elevated text-muted',
  Running: 'bg-primary/15 text-primary',
  Skipped: 'bg-elevated text-dimmed',
  notRun: 'bg-transparent ring ring-default text-dimmed',
}

function describe(link: ChainLink): string | undefined {
  const run = link.run
  if (!run) {
    return 'Never started in this pass.'
  }
  if (run.error) {
    return run.error.split('\n')[0]
  }
  const recap = summaryRecap(run.summary)
  if (recap.reason) {
    return recap.reason
  }
  return recap.fields.map(field => `${field.label} ${field.value}`).join(' · ') || undefined
}

function toItem(link: ChainLink): StepItem {
  const ran = link.run !== null && link.outcome !== 'Skipped'
  return {
    value: link.processName,
    title: chainLabel(link.processName),
    // Beside the title rather than in the item's own date line: one line less per
    // step, and the durations line up in a column a reader can scan down.
    meta: ran
      ? `${formatClock(link.run!.startedAtUtc)} · ${formatElapsed(link.run!.durationMs)}`
      : outcomeLabel(link.outcome),
    description: describe(link),
    icon: outcomeIcon(link.outcome),
    run: link.run,
    ui: {
      item: link.run ? 'cursor-pointer' : '',
      indicator: INDICATOR_CLASS[link.outcome],
      title: ran ? '' : 'text-muted font-normal',
    },
  }
}

// One timeline per lane the pass ran: one for a lane pass, two for a `Full` one.
const groups = computed(() =>
  buildLaneBranches(props.iteration.runs).map(branch => ({
    id: branch.id,
    label: branch.label,
    items: branch.links.map(toItem),
  })),
)

function onSelect(_event: Event, item: StepItem) {
  if (item.run) {
    emit('openRun', item.run)
  }
}
</script>

<template>
  <div class="space-y-4">
    <div v-for="group in groups" :key="group.id">
      <p v-if="groups.length > 1" class="text-xs font-medium text-muted mb-2">
        {{ group.label }}
      </p>
      <UTimeline
        :items="group.items"
        size="xs"
        color="neutral"
        :ui="{
          wrapper: 'pb-2.5 min-w-0 max-w-2xl',
          title: 'text-xs',
          description: 'text-xs text-dimmed truncate',
        }"
        @select="onSelect"
      >
        <template #title="{ item }">
          <span class="flex items-baseline justify-between gap-3">
            <span>{{ item.title }}</span>
            <span class="shrink-0 text-[11px] font-normal tabular-nums text-dimmed">{{ (item as StepItem).meta }}</span>
          </span>
        </template>
      </UTimeline>
    </div>
  </div>
</template>
