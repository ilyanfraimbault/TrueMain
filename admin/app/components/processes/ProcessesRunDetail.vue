<script setup lang="ts">
// Run detail slide-over of the Processes page: one run's status, timing, host,
// error and formatted summary, with the raw JSON as a collapsible fallback.
// Split out of `pages/processes.vue` (#1436).
import type { ProcessRun } from '~~/shared/types/ops'
import { formatDateTime, formatElapsed } from '~~/shared/utils/format'
import { processStatusColor, processStatusIcon } from '~~/shared/utils/pipeline-health'
import { hasSummary } from '~~/shared/utils/process-summary'

const props = defineProps<{
  run: ProcessRun | null
}>()

const open = defineModel<boolean>('open', { required: true })

const summaryHasContent = computed(() => hasSummary(props.run?.summary ?? null))
const rawSummary = computed(() => summaryJson(props.run?.summary ?? null))
</script>

<template>
  <USlideover
    v-model:open="open"
    :title="run?.processName ?? 'Run details'"
    :description="run
      ? `${run.status} · ${formatDateTime(run.startedAtUtc)}`
      : ''"
  >
    <template #body>
      <div v-if="run" class="space-y-5">
        <dl class="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
          <div>
            <dt class="text-muted text-xs uppercase mb-0.5">
              Status
            </dt>
            <dd>
              <UBadge
                :color="processStatusColor(run.status)"
                :icon="processStatusIcon(run.status)"
                :ui="statusBadgeUi(run.status)"
                variant="subtle"
                size="sm"
                :label="run.status"
              />
            </dd>
          </div>
          <div>
            <dt class="text-muted text-xs uppercase mb-0.5">
              Duration
            </dt>
            <dd class="tabular-nums">
              <span v-if="run.status === 'Running'" class="text-dimmed">
                In progress…
              </span>
              <template v-else>
                {{ formatElapsed(run.durationMs) }}
              </template>
            </dd>
          </div>
          <div>
            <dt class="text-muted text-xs uppercase mb-0.5">
              Started
            </dt>
            <dd>{{ formatDateTime(run.startedAtUtc) }}</dd>
          </div>
          <div>
            <dt class="text-muted text-xs uppercase mb-0.5">
              Finished
            </dt>
            <dd>
              <span v-if="run.status === 'Running'" class="text-dimmed">
                —
              </span>
              <template v-else>
                {{ formatDateTime(run.finishedAtUtc) }}
              </template>
            </dd>
          </div>
          <div>
            <dt class="text-muted text-xs uppercase mb-0.5">
              Host
            </dt>
            <dd class="font-mono text-xs">
              {{ run.host ?? '—' }}
            </dd>
          </div>
        </dl>

        <div v-if="run.error">
          <p class="text-muted text-xs uppercase mb-1.5">
            Error
          </p>
          <pre class="text-xs text-error bg-error/5 border border-error/20 rounded-md p-3 overflow-auto whitespace-pre-wrap">{{ run.error }}</pre>
        </div>

        <div>
          <p class="text-muted text-xs uppercase mb-1.5">
            Summary
          </p>
          <ProcessSummaryView
            v-if="summaryHasContent"
            :value="run.summary"
          />
          <p v-else class="text-sm text-dimmed">
            No summary recorded for this run.
          </p>

          <!-- Raw payload stays available for exact values / debugging. -->
          <details v-if="summaryHasContent && rawSummary" class="mt-2">
            <summary class="text-xs text-muted cursor-pointer hover:text-default">
              Raw JSON
            </summary>
            <pre class="mt-1.5 text-xs bg-elevated/50 border border-default rounded-md p-3 overflow-auto">{{ rawSummary }}</pre>
          </details>
        </div>
      </div>
    </template>
  </USlideover>
</template>
