<script setup lang="ts">
// One finished iteration as a single line of the recent-iterations list: what kind of
// pass it was, when it ran, a bar of where its time went, and how its steps ended.
// Hovering a segment of the bar recaps that step; the row expands into the step
// timeline, and the per-run summaries stay in the slide-over.
import type { ProcessIteration, ProcessRun } from '~~/shared/types/ops'
import type { IterationKindId } from '~~/shared/utils/iteration-timeline'
import { formatClock, formatDayLabel, formatElapsed } from '~~/shared/utils/format'
import { iterationKind, iterationSpan, statusCounts, timedRuns } from '~~/shared/utils/iteration-timeline'
import { summaryRecap } from '~~/shared/utils/process-summary'

const props = defineProps<{
  iteration: ProcessIteration
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

// --- Hover recap ---------------------------------------------------------------
// A hand-placed card rather than a UTooltip per segment: the bar sits inside the row's
// toggle, and a tooltip trigger on every segment swallowed the click meant for it.
// The card ignores the pointer, so hovering never gets in the way of expanding.
const hoveredIndex = ref<number | null>(null)

// The segment under the pointer, from its position along the bar: the segments are
// laid out by duration, so the same cumulative share finds it.
function onBarPointer(event: PointerEvent) {
  const bar = event.currentTarget as HTMLElement
  const width = bar.getBoundingClientRect().width
  const total = segments.value.reduce((sum, run) => sum + run.durationMs, 0)
  if (width <= 0 || total <= 0) {
    return
  }
  const target = ((event.clientX - bar.getBoundingClientRect().left) / width) * total
  let reached = 0
  const index = segments.value.findIndex((run) => {
    reached += run.durationMs
    return target < reached
  })
  hoveredIndex.value = index === -1 ? segments.value.length - 1 : index
}

const hovered = computed(() => {
  const index = hoveredIndex.value
  const run = index === null ? null : segments.value[index]
  if (!run) {
    return null
  }
  const total = segments.value.reduce((sum, r) => sum + r.durationMs, 0)
  const before = segments.value.slice(0, index!).reduce((sum, r) => sum + r.durationMs, 0)
  const recap = summaryRecap(run.summary, 2)
  return {
    run,
    // The card centres on its segment, kept inside the bar so it never hangs off an edge.
    centerPct: Math.min(85, Math.max(15, total > 0 ? ((before + run.durationMs / 2) / total) * 100 : 50)),
    sharePct: span.value.durationMs > 0 ? Math.round((run.durationMs / span.value.durationMs) * 100) : null,
    detail: run.error?.split('\n')[0] ?? recap.reason,
    fields: run.error ? [] : recap.fields,
  }
})
</script>

<template>
  <!-- No overflow clipping on the row or the list, so the hover card can rise above
       the first row; the corners are rounded by hand instead. -->
  <UCollapsible
    v-model:open="open"
    class="group/row border-t border-default first:border-t-0 first:rounded-t-lg last:rounded-b-lg"
    :class="failed ? 'bg-error/5' : ''"
  >
    <button
      type="button"
      class="group-first/row:rounded-t-lg group-last/row:data-[state=closed]:rounded-b-lg grid w-full grid-cols-[auto_1fr_auto_1rem] items-center gap-x-4 gap-y-2 px-4 py-2.5 text-left transition-colors hover:bg-elevated/40 md:grid-cols-[7.5rem_10.5rem_minmax(8rem,1fr)_4.5rem_10rem_1rem]"
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

      <span class="relative col-span-full row-start-2 md:col-span-1 md:row-start-auto">
        <span
          class="flex h-2 overflow-hidden rounded-full bg-accented/60"
          @pointermove="onBarPointer"
          @pointerleave="hoveredIndex = null"
        >
          <span
            v-for="(run, index) in segments"
            :key="run.id"
            class="h-full basis-0 border-r border-default/80 transition-opacity last:border-r-0"
            :class="[outcomeBarClass(run.status), hoveredIndex !== null && hoveredIndex !== index ? 'opacity-50' : '']"
            :style="{ flexGrow: run.durationMs }"
          />
        </span>

        <span
          v-if="hovered"
          class="pointer-events-none absolute bottom-full z-20 mb-2 w-64 -translate-x-1/2 rounded-md border border-default bg-default px-3 py-2 shadow-lg"
          :style="{ left: `${hovered.centerPct}%` }"
        >
          <span class="flex items-center gap-1.5">
            <UIcon :name="outcomeIcon(hovered.run.status)" class="size-3.5 shrink-0" :class="outcomeTextClass(hovered.run.status)" />
            <span class="text-xs font-medium text-highlighted truncate">{{ chainLabel(hovered.run.processName) }}</span>
          </span>
          <span class="mt-1 block text-[11px] tabular-nums text-muted">
            {{ formatClock(hovered.run.startedAtUtc) }} → {{ formatClock(hovered.run.finishedAtUtc) }}
            · {{ formatElapsed(hovered.run.durationMs) }}<template v-if="hovered.sharePct !== null"> · {{ hovered.sharePct }}% of the pass</template>
          </span>
          <span v-if="hovered.detail" class="mt-1 block text-[11px] text-dimmed line-clamp-2">{{ hovered.detail }}</span>
          <span v-for="field in hovered.fields" :key="field.label" class="mt-0.5 flex justify-between gap-3 text-[11px]">
            <span class="text-dimmed truncate">{{ field.label }}</span>
            <span class="tabular-nums text-default">{{ field.value }}</span>
          </span>
        </span>
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
    </button>

    <template #content>
      <div class="px-4 pb-3 pt-2 md:pl-[9.5rem]">
        <ProcessesIterationSteps :iteration="iteration" @open-run="$emit('openRun', $event)" />
        <div class="flex justify-end">
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
