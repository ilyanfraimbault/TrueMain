<script setup lang="ts">
// Recent iterations of the Processes page: each finished pass as the chain with
// per-process outcomes, newest first, paged independently of the runs table.
// Split out of `pages/processes.vue` (#1436); the page owns the fetch and the
// page number.
import type { ProcessIteration } from '~~/shared/types/ops'
import type { LaneBranch } from '~~/shared/utils/pipeline-lanes'
import { formatDateTime, formatElapsed } from '~~/shared/utils/format'
import { buildLaneBranches } from '~~/shared/utils/pipeline-lanes'

const props = defineProps<{
  iterations: ProcessIteration[]
  pending: boolean
  error: unknown
  total: number
  pageSize: number
  /** The page the server actually served (its clamp wins over the model). */
  serverPage: number
}>()

const page = defineModel<number>('page', { required: true })

defineEmits<{
  openIteration: [iteration: ProcessIteration]
}>()

// Precompute each finished iteration's branches once, so the template shares one
// array between its v-for and its separator length check.
const iterationBranches = computed<Map<string, LaneBranch[]>>(
  () => new Map(props.iterations.map(it => [it.iterationId, buildLaneBranches(it.runs)])),
)
</script>

<template>
  <div class="mb-6">
    <PanelTitle
      variant="label"
      title="Recent iterations"
      subtitle="Newest first."
      class="mb-3"
    />

    <div
      v-if="error"
      class="py-8 text-center text-sm text-muted border border-default rounded-lg"
    >
      Failed to load recent iterations.
    </div>
    <div v-else-if="pending" class="space-y-3">
      <USkeleton v-for="i in 3" :key="i" class="h-16 w-full rounded-lg" />
    </div>
    <div
      v-else-if="iterations.length === 0"
      class="py-8 text-center text-sm text-muted border border-default rounded-lg"
    >
      No finished iterations yet.
    </div>
    <div v-else class="space-y-3">
      <button
        v-for="iteration in iterations"
        :key="iteration.iterationId"
        type="button"
        class="block w-full text-left rounded-lg border border-default p-4 bg-elevated/25 transition-colors hover:bg-elevated/50 hover:border-primary/40"
        title="View iteration summary"
        @click="$emit('openIteration', iteration)"
      >
        <div class="flex items-center justify-between gap-2 mb-3">
          <span class="text-sm text-muted">
            {{ formatDateTime(iteration.startedAtUtc) }}
          </span>
          <UIcon name="i-lucide-chevron-right" class="size-4 text-dimmed shrink-0" />
        </div>

        <!-- One branch per lane the pass ran — normally exactly one, since a
             pass belongs to a lane; a `Full` deployment shows both. -->
        <div
          v-for="branch in iterationBranches.get(iteration.iterationId)"
          :key="branch.id"
          class="relative border-l-2 pl-3 not-first:mt-3"
          :class="laneRailClass(branch.outcome)"
        >
          <div class="flex flex-wrap items-center gap-x-2 gap-y-1 mb-1.5">
            <span class="text-xs font-medium text-muted">{{ branch.label }}</span>
            <span v-if="branch.durationMs !== null" class="text-[11px] text-dimmed tabular-nums">
              · {{ formatElapsed(branch.durationMs) }}
            </span>
          </div>

          <div class="flex flex-wrap items-center gap-y-2">
            <template
              v-for="(link, i) in branch.links"
              :key="link.processName"
            >
              <UTooltip :ui="PROCESS_TOOLTIP_UI">
                <span
                  class="inline-flex items-center gap-1.5 rounded-md border px-2 py-1"
                  :class="{
                    'border-primary/50 bg-primary/10': link.outcome === 'Running',
                    'border-success/30 bg-success/5': link.outcome === 'Success',
                    'border-error/40 bg-error/10': link.outcome === 'Failed',
                    'border-warning/40 bg-warning/10': link.outcome === 'Abandoned',
                    'border-default bg-default opacity-50': link.outcome === 'notRun',
                  }"
                >
                  <UIcon
                    :name="outcomeIcon(link.outcome)"
                    class="size-3.5 shrink-0"
                    :class="outcomeTextClass(link.outcome)"
                  />
                  <span
                    class="text-[11px] font-medium whitespace-nowrap"
                    :class="link.outcome === 'notRun' ? 'text-dimmed' : 'text-default'"
                  >
                    {{ chainLabel(link.processName) }}
                  </span>
                </span>
                <template #content>
                  <ProcessTooltipContent :process-name="link.processName" :context="iterationChipContext(link)" />
                </template>
              </UTooltip>
              <UIcon
                v-if="i < branch.links.length - 1"
                name="i-lucide-chevron-right"
                class="size-3.5 text-dimmed shrink-0 mx-0.5"
              />
            </template>
          </div>
        </div>
      </button>
    </div>

    <!-- Iteration pagination -->
    <div
      v-if="total > pageSize"
      class="flex items-center justify-between gap-2 mt-4"
    >
      <p class="text-xs text-muted tabular-nums">
        Page {{ serverPage.toLocaleString('en-US') }} of
        {{ Math.max(1, Math.ceil(total / pageSize)).toLocaleString('en-US') }}
      </p>
      <UPagination
        v-model:page="page"
        :total="total"
        :items-per-page="pageSize"
        :sibling-count="1"
        active-color="primary"
        variant="subtle"
        :disabled="pending"
      />
    </div>
  </div>
</template>
