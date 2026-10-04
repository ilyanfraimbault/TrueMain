<script setup lang="ts">
// Iteration detail slide-over of the Processes page: a formatted per-process
// breakdown of one finished pass (outcome, duration, summary fields, error)
// instead of raw JSON. Split out of `pages/processes.vue` (#1436).
import type { BadgeColor, ProcessIteration, ProcessRunStatus } from '~~/shared/types/ops'
import type { ChainLink } from '~~/shared/utils/pipeline-lanes'
import { formatDateTime, formatElapsed } from '~~/shared/utils/format'
import { processStatusColor } from '~~/shared/utils/pipeline-health'
import { buildLaneBranches } from '~~/shared/utils/pipeline-lanes'
import { hasSummary } from '~~/shared/utils/process-summary'

const props = defineProps<{
  iteration: ProcessIteration | null
}>()

const open = defineModel<boolean>('open', { required: true })

// The iteration's runs in canonical order, keeping only processes that actually
// ran this pass (skip `notRun` placeholders in the detail view). Flattened
// across lanes: a finished iteration belongs to one lane anyway, and the detail
// view is a list of what ran, not a topology.
const links = computed<ChainLink[]>(() =>
  props.iteration
    ? buildLaneBranches(props.iteration.runs)
      .flatMap(branch => branch.links)
      .filter(link => link.run)
    : [],
)

// Precompute each entry's summary state once per iteration, so the template
// doesn't recompute hasSummary/summaryJson twice per row on every render. The
// run's `summary` is handed straight to `ProcessSummaryView` for rendering.
interface IterationEntry {
  link: ChainLink
  hasSummary: boolean
  json: string | null
}
const entries = computed<IterationEntry[]>(() =>
  links.value.map(link => ({
    link,
    hasSummary: hasSummary(link.run?.summary ?? null),
    json: summaryJson(link.run?.summary ?? null),
  })),
)

// Per-iteration outcome tallies for the detail header. Only finished iterations reach
// this view (the in-flight one is excluded from the list), so there is no `running`
// bucket — every run has settled. Rendered from one list rather than a badge per status:
// the five were the same markup five times, and a status added later (Cancelled was, in
// #1513) is a row here instead of a sixth copy. `Success` shows even at zero — "0 ok" is
// the point when a pass went wrong — while the rest appear only when they happened.
const TALLIED_STATUSES: readonly { status: ProcessRunStatus, label: string, color: BadgeColor, always?: boolean }[] = [
  { status: 'Success', label: 'ok', color: 'success', always: true },
  { status: 'Skipped', label: 'skipped', color: 'neutral' },
  { status: 'Failed', label: 'failed', color: 'error' },
  { status: 'Abandoned', label: 'abandoned', color: 'warning' },
  { status: 'Cancelled', label: 'cancelled', color: 'neutral' },
]

const tally = computed(() => {
  const runs = props.iteration?.runs ?? []
  return TALLIED_STATUSES
    .map(entry => ({ ...entry, count: runs.filter(run => run.status === entry.status).length }))
    .filter(entry => entry.always || entry.count > 0)
})
</script>

<template>
  <USlideover
    v-model:open="open"
    :title="iteration ? 'Iteration summary' : 'Iteration'"
    :description="iteration ? formatDateTime(iteration.startedAtUtc) : ''"
  >
    <template #body>
      <div v-if="iteration" class="space-y-5">
        <!-- Outcome tally -->
        <div class="flex flex-wrap gap-2">
          <UBadge
            v-for="entry in tally"
            :key="entry.status"
            :color="entry.color"
            variant="subtle"
            size="sm"
            :label="`${entry.count} ${entry.label}`"
          />
        </div>

        <p
          v-if="links.length === 0"
          class="text-sm text-dimmed"
        >
          No processes ran in this iteration.
        </p>

        <!-- One collapsible entry per process that ran -->
        <details
          v-for="entry in entries"
          :key="entry.link.processName"
          class="rounded-lg border border-default bg-elevated/25 overflow-hidden"
        >
          <summary class="flex items-center justify-between gap-2 px-3 py-2.5 cursor-pointer hover:bg-elevated/40">
            <span class="flex items-center gap-2 min-w-0">
              <UIcon
                :name="outcomeIcon(entry.link.outcome)"
                class="size-4 shrink-0"
                :class="{
                  'text-primary': entry.link.outcome === 'Running',
                  'text-success': entry.link.outcome === 'Success',
                  'text-error': entry.link.outcome === 'Failed',
                  'text-warning': entry.link.outcome === 'Abandoned',
                }"
              />
              <UTooltip :ui="PROCESS_TOOLTIP_UI">
                <span class="text-sm font-medium text-highlighted truncate">
                  {{ chainLabel(entry.link.processName) }}
                </span>
                <template #content>
                  <ProcessTooltipContent :process-name="entry.link.processName" :context="entry.link.processName" />
                </template>
              </UTooltip>
            </span>
            <span class="flex items-center gap-2 shrink-0">
              <span class="text-xs text-dimmed tabular-nums">
                {{ entry.link.run && entry.link.outcome !== 'Running' ? formatElapsed(entry.link.run.durationMs) : '—' }}
              </span>
              <UBadge
                v-if="entry.link.run"
                :color="processStatusColor(entry.link.run.status)"
                variant="subtle"
                size="sm"
                :label="entry.link.run.status"
              />
            </span>
          </summary>

          <div v-if="entry.link.run" class="px-3 pb-3 pt-1 space-y-3 border-t border-default">
            <div v-if="entry.link.run.error">
              <p class="text-muted text-xs uppercase mb-1.5">
                Error
              </p>
              <pre class="text-xs text-error bg-error/5 border border-error/20 rounded-md p-2.5 overflow-auto whitespace-pre-wrap">{{ entry.link.run.error }}</pre>
            </div>

            <ProcessSummaryView
              v-if="entry.hasSummary"
              :value="entry.link.run?.summary"
            />
            <p v-else-if="!entry.link.run.error" class="text-sm text-dimmed">
              No summary recorded.
            </p>

            <!-- Raw payload stays available for exact values / debugging. -->
            <details v-if="entry.hasSummary && entry.json" class="mt-1">
              <summary class="text-xs text-muted cursor-pointer hover:text-default">
                Raw JSON
              </summary>
              <pre class="mt-1.5 text-xs bg-elevated/50 border border-default rounded-md p-3 overflow-auto">{{ entry.json }}</pre>
            </details>
          </div>
        </details>
      </div>
    </template>
  </USlideover>
</template>
