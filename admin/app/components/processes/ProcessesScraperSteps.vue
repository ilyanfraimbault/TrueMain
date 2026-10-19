<script setup lang="ts">
// The phases of one ops-script run (#2119), in the order the script ran them: each with
// its status, how long it took (or has been running) and its own counters. Shared by the
// Scrapers tab and the run detail slide-over.
import type { ScraperProgress, ScraperStep, ScraperStepStatus } from '~~/shared/utils/scraper-runs'
import { formatElapsed, formatNumber } from '~~/shared/utils/format'
import { humanizeKey } from '~~/shared/utils/process-summary'
import { progressPercent } from '~~/shared/utils/scraper-runs'

const props = defineProps<{
  steps: ScraperStep[]
  progress?: ScraperProgress | null
  /** Whether the run is still in flight: a `running` step of a dead run is not ticking. */
  live?: boolean
}>()

const STATUS_META: Record<ScraperStepStatus, { icon: string, class: string }> = {
  running: { icon: 'i-lucide-loader-circle', class: 'text-info animate-spin' },
  done: { icon: 'i-lucide-circle-check', class: 'text-success' },
  failed: { icon: 'i-lucide-circle-x', class: 'text-error' },
  skipped: { icon: 'i-lucide-circle-minus', class: 'text-dimmed' },
}

const now = ref(Date.now())
useIntervalFn(() => {
  now.value = Date.now()
}, 1000)

function stepDuration(step: ScraperStep): string {
  if (!step.startedAtUtc) {
    return ''
  }
  const start = new Date(step.startedAtUtc).getTime()
  const end = step.finishedAtUtc
    ? new Date(step.finishedAtUtc).getTime()
    : (step.status === 'running' && props.live ? now.value : Number.NaN)
  return Number.isNaN(start) || Number.isNaN(end) ? '' : formatElapsed(end - start)
}

function counterValue(value: number | string): string {
  return typeof value === 'number' ? formatNumber(value) : value
}

const percent = computed(() => progressPercent(props.progress ?? null))
</script>

<template>
  <ol class="space-y-3">
    <li v-for="step in steps" :key="step.name" class="flex gap-3">
      <UIcon
        :name="STATUS_META[step.status].icon"
        class="size-4 mt-0.5 shrink-0"
        :class="step.status === 'running' && !live ? 'text-warning' : STATUS_META[step.status].class"
      />
      <div class="min-w-0 flex-1 space-y-1.5">
        <div class="flex items-baseline justify-between gap-3">
          <span class="text-sm font-medium">{{ step.label ?? humanizeKey(step.name) }}</span>
          <span class="text-xs text-muted tabular-nums">{{ stepDuration(step) }}</span>
        </div>
        <template v-if="step.status === 'running' && progress">
          <UProgress v-if="percent !== null" :model-value="percent" size="xs" />
          <p class="text-xs text-muted tabular-nums">
            {{ formatNumber(progress.done) }}<template v-if="progress.total !== null">
              / {{ formatNumber(progress.total) }} ({{ percent }} %)
            </template>
            <template v-if="progress.label">
              · {{ progress.label }}
            </template>
          </p>
        </template>
        <div v-if="Object.keys(step.counters).length" class="flex flex-wrap gap-1.5">
          <UBadge
            v-for="(value, key) in step.counters"
            :key="key"
            color="neutral"
            variant="soft"
            size="sm"
            class="tabular-nums"
            :label="`${humanizeKey(String(key))}: ${counterValue(value)}`"
          />
        </div>
      </div>
    </li>
  </ol>
</template>
