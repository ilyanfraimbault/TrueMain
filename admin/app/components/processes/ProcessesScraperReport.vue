<script setup lang="ts">
// The readable part of an ops-script run report (#2125): how the run was invoked, its
// headline numbers in labelled groups, and its errors per source. Shared by the Scrapers
// card and the run slide-over; the steps timeline sits next to it, not in it.
import type { ScraperResultTone, ScraperSummary } from '~~/shared/utils/scraper-runs'
import { computed } from 'vue'
import { formatNumber } from '~~/shared/utils/format'
import { humanizeKey } from '~~/shared/utils/process-summary'
import { formatRunArg } from '~~/shared/utils/scraper-runs'

const props = defineProps<{
  summary: ScraperSummary
  /** Show the invocation block (the card already names the run in its header). */
  showArgs?: boolean
}>()

const TONE_CLASS: Record<ScraperResultTone, string> = {
  success: 'text-success',
  warning: 'text-warning',
  error: 'text-error',
}

const args = computed(() => Object.entries(props.summary.run?.args ?? {}))
const errorTotal = computed(() => props.summary.errors.reduce((sum, e) => sum + e.count, 0))

function value(v: number | string): string {
  return typeof v === 'number' ? formatNumber(v) : v
}
</script>

<template>
  <div class="@container space-y-5">
    <div v-if="showArgs && args.length">
      <p class="text-muted text-xs uppercase mb-1.5">
        Invocation
      </p>
      <dl class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <template v-for="[key, arg] in args" :key="key">
          <dt class="text-muted">
            {{ humanizeKey(key) }}
          </dt>
          <dd class="font-mono text-xs self-center break-words">
            {{ formatRunArg(arg) }}
          </dd>
        </template>
      </dl>
    </div>

    <div v-if="summary.results.length" class="grid gap-4 @lg:grid-cols-2 @3xl:grid-cols-3">
      <section
        v-for="group in summary.results"
        :key="group.label"
        class="rounded-md border border-default p-3"
      >
        <p class="text-muted text-xs uppercase mb-2">
          {{ group.label }}
        </p>
        <dl class="space-y-1 text-sm">
          <div v-for="entry in group.items" :key="entry.label" class="flex items-baseline justify-between gap-3">
            <dt class="text-muted">
              {{ entry.label }}
            </dt>
            <dd class="tabular-nums font-medium" :class="entry.tone ? TONE_CLASS[entry.tone] : ''">
              {{ value(entry.value) }}
            </dd>
          </div>
        </dl>
      </section>
    </div>

    <div v-if="summary.errors.length" class="rounded-md border border-error/30 bg-error/5 p-3 space-y-3">
      <p class="text-xs uppercase text-error">
        {{ formatNumber(errorTotal) }} error{{ errorTotal === 1 ? '' : 's' }} across {{ summary.errors.length }} source{{ summary.errors.length === 1 ? '' : 's' }}
      </p>
      <div v-for="source in summary.errors" :key="source.source" class="space-y-1">
        <p class="text-sm">
          <span class="font-medium">{{ source.label ?? humanizeKey(source.source) }}</span>
          <span class="text-muted"> — {{ formatNumber(source.count) }}</span>
          <span v-if="source.blocked" class="text-error"> · dropped for the run: {{ source.blocked }}</span>
        </p>
        <ul v-if="source.last.length" class="space-y-0.5">
          <li
            v-for="(message, i) in source.last"
            :key="i"
            class="font-mono text-xs text-muted truncate"
            :title="message"
          >
            {{ message }}
          </li>
        </ul>
      </div>
    </div>
  </div>
</template>
