<script setup lang="ts">
// One ops script on the Scrapers tab (#2119): its latest run — status, timing, host, the
// phase in flight with its progress, every phase's counters — then its recent history.
import type { ProcessRun, ProcessRunsResponse } from '~~/shared/types/ops'
import type { ScraperProcess } from '~~/shared/utils/scraper-runs'
import { formatDateTime, formatElapsed, formatTimeAgo } from '~~/shared/utils/format'
import { processStatusColor, processStatusIcon } from '~~/shared/utils/pipeline-health'
import { parseScraperSummary } from '~~/shared/utils/scraper-runs'
import { useIntervalFn } from '@vueuse/core'
import { computed, ref } from 'vue'
import { statusBadgeUi } from '~/utils/process-chain'

const props = defineProps<{
  scraper: ScraperProcess
  data: ProcessRunsResponse | null | undefined
  pending: boolean
  error: unknown
}>()

const emit = defineEmits<{ openRun: [run: ProcessRun] }>()

const runs = computed(() => props.data?.runs ?? [])
const latest = computed(() => runs.value[0] ?? null)
const history = computed(() => runs.value.slice(1))
const parsed = computed(() => parseScraperSummary(latest.value?.summary ?? null))
const isRunning = computed(() => latest.value?.status === 'Running')

// Literal classes, so Tailwind sees them (a `text-${color}` template would be purged).
const ICON_CLASS: Record<string, string> = {
  success: 'text-success',
  error: 'text-error',
  info: 'text-info',
  warning: 'text-warning',
  neutral: 'text-muted',
}

const now = ref(Date.now())
useIntervalFn(() => {
  now.value = Date.now()
}, 1000)

function runDuration(run: ProcessRun): string {
  if (run.status !== 'Running') {
    return formatElapsed(run.durationMs)
  }
  return formatElapsed(now.value - new Date(run.startedAtUtc).getTime())
}
</script>

<template>
  <UCard>
    <template #header>
      <div class="flex items-start justify-between gap-4">
        <div class="min-w-0">
          <PanelTitle :title="scraper.label" :subtitle="scraper.description" />
        </div>
        <UBadge
          v-if="latest"
          :color="processStatusColor(latest.status)"
          :icon="processStatusIcon(latest.status)"
          :ui="statusBadgeUi(latest.status)"
          variant="subtle"
          :label="latest.status"
        />
      </div>
    </template>

    <FetchErrorAlert v-if="error" :error="error" :title="`Failed to load ${scraper.label} runs`" />

    <div v-else-if="!latest" class="text-sm text-dimmed">
      {{ pending ? 'Loading…' : 'No run reported yet.' }}
    </div>

    <div v-else class="space-y-5">
      <dl class="grid grid-cols-2 sm:grid-cols-4 gap-x-4 gap-y-3 text-sm">
        <div>
          <dt class="text-muted text-xs uppercase mb-0.5">
            Started
          </dt>
          <dd :title="formatDateTime(latest.startedAtUtc)">
            {{ formatTimeAgo(latest.startedAtUtc) }}
          </dd>
        </div>
        <div>
          <dt class="text-muted text-xs uppercase mb-0.5">
            {{ isRunning ? 'Running for' : 'Duration' }}
          </dt>
          <dd class="tabular-nums">
            {{ runDuration(latest) }}
          </dd>
        </div>
        <div>
          <dt class="text-muted text-xs uppercase mb-0.5">
            Current step
          </dt>
          <dd>{{ isRunning ? (parsed.currentStep ?? '—') : '—' }}</dd>
        </div>
        <div>
          <dt class="text-muted text-xs uppercase mb-0.5">
            Host
          </dt>
          <dd class="font-mono text-xs truncate">
            {{ latest.host ?? '—' }}
          </dd>
        </div>
      </dl>

      <pre v-if="latest.error" class="text-xs text-error bg-error/5 border border-error/20 rounded-md p-3 overflow-auto whitespace-pre-wrap">{{ latest.error }}</pre>

      <ProcessesScraperSteps
        v-if="parsed.steps.length"
        :steps="parsed.steps"
        :progress="parsed.progress"
        :live="isRunning"
      />

      <UButton
        color="neutral"
        variant="link"
        size="sm"
        icon="i-lucide-file-text"
        label="Full report"
        class="px-0"
        @click="emit('openRun', latest)"
      />

      <div v-if="history.length">
        <p class="text-muted text-xs uppercase mb-1.5">
          Previous runs
        </p>
        <ul class="divide-y divide-default">
          <li v-for="run in history" :key="run.id">
            <button
              type="button"
              class="w-full flex items-center gap-3 py-1.5 text-sm text-left hover:bg-elevated/50 rounded-sm"
              @click="emit('openRun', run)"
            >
              <UIcon
                :name="processStatusIcon(run.status)"
                class="size-4 shrink-0"
                :class="ICON_CLASS[processStatusColor(run.status)] ?? 'text-muted'"
              />
              <span class="flex-1">{{ formatDateTime(run.startedAtUtc) }}</span>
              <span class="text-xs text-muted tabular-nums">{{ runDuration(run) }}</span>
            </button>
          </li>
        </ul>
      </div>
    </div>
  </UCard>
</template>
