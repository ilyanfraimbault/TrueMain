<script setup lang="ts">
// One issue-type card in the Data Quality panel. Owns its OWN pagination and
// fetches its OWN paged slice (`issue=<type>&page=<n>`), so a small group never
// renders an empty page just because a sibling group has more rows — each group
// paginates independently against its own match count.
import type { TableColumn } from '@nuxt/ui'
import type {
  DataQualityIssueType,
  FlaggedMatch,
  IncompleteMatchesFilters,
  IssueMeta,
} from '~~/shared/types/ops'
import { formatDateTime } from '~~/shared/utils/format'

const props = defineProps<{
  issueType: DataQualityIssueType
  /** Total matches flagged by this check (from the parent's overview fetch). */
  count: number
  /** Shared filters (queue / age); this card pins `issue` and its own `page`. */
  baseFilters: Omit<IncompleteMatchesFilters, 'issue' | 'page'>
  pageSize: number
  meta: Record<DataQualityIssueType, IssueMeta>
  queueLabel: (queueId: number) => string
}>()

const emit = defineEmits<{ select: [matchId: string] }>()

// This card's own page; reset to 1 whenever the shared filters change so a
// narrower filter can't strand the card on a now-out-of-range page.
const page = ref(1)
watch(
  () => props.baseFilters,
  () => { page.value = 1 },
  { deep: true },
)

const filters = computed<IncompleteMatchesFilters>(() => ({
  ...props.baseFilters,
  issue: props.issueType,
  page: page.value,
  pageSize: props.pageSize,
}))

const { data, pending } = useIncompleteMatches(filters)

// The single-issue response carries exactly this group (or none, if a filter
// pushed the count to zero between the overview fetch and this one).
const group = computed(() => data.value?.groups.find(g => g.issueType === props.issueType))
const matches = computed<FlaggedMatch[]>(() => group.value?.matches ?? [])
// Prefer the live per-issue count, falling back to the parent's overview count.
const liveCount = computed(() => group.value?.count ?? props.count)

const issueMeta = computed(() => props.meta[props.issueType])

const columns: TableColumn<FlaggedMatch>[] = [
  { accessorKey: 'matchId', header: 'Match' },
  { accessorKey: 'platformId', header: 'Region' },
  { accessorKey: 'queueId', header: 'Queue' },
  { accessorKey: 'gameStartTimeUtc', header: 'Played' },
  { accessorKey: 'participantCount', header: 'Players' },
  { accessorKey: 'issues', header: 'Also flagged' },
]
</script>

<template>
  <UCard :ui="{ body: 'p-0 sm:p-0' }">
    <template #header>
      <div class="flex items-center justify-between gap-3">
        <div class="flex items-center gap-2.5 min-w-0">
          <UIcon
            :name="issueMeta.icon"
            class="size-5 shrink-0"
            :class="{
              'text-error': issueMeta.color === 'error',
              'text-warning': issueMeta.color === 'warning',
              'text-info': issueMeta.color === 'info',
            }"
          />
          <div class="min-w-0">
            <p class="text-sm font-medium text-highlighted">
              {{ issueMeta.label }}
            </p>
            <p class="text-xs text-muted line-clamp-1">
              {{ issueMeta.description }}
            </p>
          </div>
        </div>
        <UBadge
          :color="issueMeta.color"
          variant="subtle"
          :label="`${liveCount.toLocaleString('en-US')} ${liveCount === 1 ? 'match' : 'matches'}`"
        />
      </div>
    </template>

    <UTable
      :data="matches"
      :columns="columns"
      :loading="pending"
      :ui="{ td: 'py-2', tr: 'cursor-pointer' }"
      class="max-h-[420px]"
      sticky
      @select="(_event, row) => emit('select', row.original.matchId)"
    >
      <template #matchId-cell="{ row }">
        <span class="font-mono text-xs text-highlighted">
          {{ row.original.matchId }}
        </span>
      </template>
      <template #platformId-cell="{ row }">
        <span class="text-muted font-mono text-xs">
          {{ row.original.platformId }}
        </span>
      </template>
      <template #queueId-cell="{ row }">
        <span class="text-muted text-xs">
          {{ queueLabel(row.original.queueId) }}
        </span>
      </template>
      <template #gameStartTimeUtc-cell="{ row }">
        <span class="text-muted whitespace-nowrap text-xs tabular-nums">
          {{ formatDateTime(row.original.gameStartTimeUtc) }}
        </span>
      </template>
      <template #participantCount-cell="{ row }">
        <span
          class="tabular-nums text-xs"
          :class="row.original.expectedParticipantCount !== null
            && row.original.participantCount !== row.original.expectedParticipantCount
            ? 'text-error font-medium'
            : 'text-muted'"
        >
          {{ row.original.participantCount }}<template
            v-if="row.original.expectedParticipantCount !== null"
          >&nbsp;/&nbsp;{{ row.original.expectedParticipantCount }}</template>
        </span>
      </template>
      <template #issues-cell="{ row }">
        <div class="flex flex-wrap gap-1">
          <UBadge
            v-for="other in row.original.issues.filter(i => i !== issueType)"
            :key="other"
            :color="meta[other].color"
            variant="soft"
            size="sm"
            :icon="meta[other].icon"
            :label="meta[other].label"
          />
          <span
            v-if="row.original.issues.length <= 1"
            class="text-dimmed text-xs"
          >—</span>
        </div>
      </template>

      <template #empty>
        <div class="py-8 text-center text-sm text-muted">
          No matches on this page.
        </div>
      </template>
    </UTable>

    <!-- Independent paginator: only shown when this group spans >1 page, so a
         group that fits on one page never offers an empty second page. -->
    <template v-if="liveCount > pageSize" #footer>
      <div class="flex items-center justify-end">
        <UPagination
          v-model:page="page"
          :total="liveCount"
          :items-per-page="pageSize"
          :sibling-count="1"
          active-color="primary"
          variant="subtle"
          :disabled="pending"
        />
      </div>
    </template>
  </UCard>
</template>
