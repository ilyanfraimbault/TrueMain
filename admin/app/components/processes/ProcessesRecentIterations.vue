<script setup lang="ts">
// Recent iterations of the Processes page: each finished pass on one line, newest
// first, paged independently of the runs table. Split out of `pages/processes.vue`
// (#1436); the page owns the fetch, the page number and the lane filter.
//
// A pass used to be drawn as its full chain of named chips, the fetch and aggregate
// lanes interleaved: every row was a paragraph, a skipped step stood out more than a
// successful one, and nothing showed which passes ran at the same time. A row now
// says what kind of pass it was, where its time went and how its steps ended; it
// names the other lane's passes it overlapped, and expands into the step waterfall.
import type { ProcessIteration, ProcessRun } from '~~/shared/types/ops'
import type { PipelineLaneId } from '~~/shared/types/pipeline-chain'
import { parallelPasses } from '~~/shared/utils/iteration-timeline'

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
const lane = defineModel<'all' | PipelineLaneId>('lane', { required: true })

defineEmits<{
  openIteration: [iteration: ProcessIteration]
  openRun: [run: ProcessRun]
}>()

const laneTabs = [
  { label: 'All', value: 'all' },
  { label: 'Fetch', value: 'fetch', icon: 'i-lucide-download' },
  { label: 'Aggregate', value: 'aggregate', icon: 'i-lucide-layers' },
]

// Overlaps are only looked for on the unfiltered list: with one lane shown, the other
// lane's passes are not on the page to be named.
const parallelById = computed<Map<string, ProcessIteration[]>>(() =>
  new Map(props.iterations.map(it => [
    it.iterationId,
    lane.value === 'all' ? parallelPasses(it, props.iterations) : [],
  ])),
)

// Expanded rows survive a page change only by id, so a row from another page never
// opens by accident; a filter or page change simply starts collapsed.
const expanded = ref<Set<string>>(new Set())
watch([page, lane], () => {
  expanded.value = new Set()
})
function setExpanded(id: string, value: boolean) {
  const next = new Set(expanded.value)
  if (value) {
    next.add(id)
  }
  else {
    next.delete(id)
  }
  expanded.value = next
}
</script>

<template>
  <div class="mb-6">
    <div class="flex flex-wrap items-end justify-between gap-2 mb-3">
      <PanelTitle
        variant="label"
        title="Recent iterations"
        subtitle="Finished passes, newest first."
        info="Each bar segment is one step, as wide as it took; skipped steps take no
          time and are counted instead. Expand a pass for its steps."
      />
      <UTabs
        v-model="lane"
        :items="laneTabs"
        :content="false"
        size="xs"
        variant="pill"
        color="neutral"
      />
    </div>

    <div
      v-if="error"
      class="py-8 text-center text-sm text-muted border border-default rounded-lg"
    >
      Failed to load recent iterations.
    </div>
    <div v-else-if="pending" class="space-y-2">
      <USkeleton v-for="i in 4" :key="i" class="h-10 w-full rounded-lg" />
    </div>
    <div
      v-else-if="iterations.length === 0"
      class="py-8 text-center text-sm text-muted border border-default rounded-lg"
    >
      {{ lane === 'all' ? 'No finished iterations yet.' : 'No finished iterations of this lane yet.' }}
    </div>
    <div v-else class="rounded-lg border border-default bg-elevated/25 overflow-hidden">
      <ProcessesIterationRow
        v-for="iteration in iterations"
        :key="iteration.iterationId"
        :iteration="iteration"
        :parallel="parallelById.get(iteration.iterationId) ?? []"
        :open="expanded.has(iteration.iterationId)"
        @update:open="setExpanded(iteration.iterationId, $event)"
        @open-run="$emit('openRun', $event)"
        @open-iteration="$emit('openIteration', $event)"
      />
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
