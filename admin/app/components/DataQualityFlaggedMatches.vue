<script setup lang="ts">
// The Data Quality page's flagged matches, with the controls that actually govern
// them: the match-ID search and the issue/queue/age filters live in this section
// because they have never applied to the detectors, which audit the whole corpus
// rather than a queue-and-age slice. Grouped by issue type in an accordion so one
// table shows at a time. The page owns the filters (its overview fetch and the
// verdict read them) and the match slide-over this section asks it to open.
import type {
  BadgeColor,
  DataQualityIssueType,
  IncompleteMatchesFilters,
  DataQualityIssueGroup,
} from '~~/shared/types/ops'

const props = defineProps<{
  groups: DataQualityIssueGroup[]
  pending: boolean
  error: unknown
  staleHours: number
  hasActiveFilters: boolean
  baseFilters: Omit<IncompleteMatchesFilters, 'issue' | 'page'>
  pageSize: number
}>()

const emit = defineEmits<{ inspect: [matchId: string] }>()

const issue = defineModel<'all' | DataQualityIssueType>('issue', { required: true })
const queue = defineModel<string>('queue', { required: true })
const ageWindow = defineModel<'all' | '6' | '24' | '72' | '168'>('ageWindow', { required: true })
const matchIdInput = defineModel<string>('matchId', { required: true })

const issueItems = [
  { label: 'All issues', value: ALL },
  ...DATA_QUALITY_ISSUE_ORDER.map(type => ({ label: DATA_QUALITY_ISSUE_META[type].label, value: type })),
]

// Queues that have a data-quality profile on the backend (count/position rules).
const queueItems = [
  { label: 'All queues', value: ALL },
  { label: 'Ranked Solo (420)', value: '420' },
  { label: 'Ranked Flex (440)', value: '440' },
  { label: 'Normal (430)', value: '430' },
  { label: 'ARAM (450)', value: '450' },
  { label: 'Clash (700)', value: '700' },
]

const ageItems = [
  { label: 'Any age', value: ALL },
  { label: 'Older than 6h', value: '6' },
  { label: 'Older than 24h', value: '24' },
  { label: 'Older than 3 days', value: '72' },
  { label: 'Older than 7 days', value: '168' },
]

function resetFilters() {
  issue.value = ALL
  queue.value = ALL
  ageWindow.value = ALL
}

function submitMatchSearch() {
  if (matchIdInput.value.trim()) {
    emit('inspect', matchIdInput.value)
  }
}

// Worst first, so the group the accordion opens on arrival is the one worth
// opening: hard inconsistencies (error) before soft ones (warning), then the
// biggest group.
const ISSUE_SEVERITY: Record<BadgeColor, number> = {
  error: 0,
  warning: 1,
  info: 2,
  primary: 3,
  success: 4,
  neutral: 5,
}
const ISSUE_ICON_CLASS: Record<BadgeColor, string> = {
  error: 'text-error',
  warning: 'text-warning',
  info: 'text-info',
  primary: 'text-primary',
  success: 'text-success',
  neutral: 'text-muted',
}

const groupItems = computed(() => [...props.groups]
  .sort((a, b) => {
    const severity = ISSUE_SEVERITY[DATA_QUALITY_ISSUE_META[a.issueType].color]
      - ISSUE_SEVERITY[DATA_QUALITY_ISSUE_META[b.issueType].color]
    return severity !== 0 ? severity : b.count - a.count
  })
  .map(group => ({
    value: group.issueType,
    group,
    meta: DATA_QUALITY_ISSUE_META[group.issueType],
  })))

// One group open at a time. Re-pinned to the worst group whenever the current
// one leaves the list (a filter change), so the accordion is never left showing
// nothing with groups available underneath it.
const openGroup = ref('')
watch(
  groupItems,
  (items) => {
    if (!items.some(item => item.value === openGroup.value)) {
      openGroup.value = items[0]?.value ?? ''
    }
  },
  { immediate: true },
)
</script>

<template>
  <section>
    <div class="mb-3 flex flex-wrap items-center justify-between gap-3">
      <h2 class="text-sm font-medium text-highlighted">
        Flagged matches
      </h2>
      <div class="flex items-center gap-2">
        <UInput
          v-model="matchIdInput"
          icon="i-lucide-search"
          placeholder="Inspect a match by ID (e.g. EUW1_1234567890)"
          class="w-64 font-mono sm:w-80"
          @keydown.enter="submitMatchSearch"
        />
        <UButton
          icon="i-lucide-arrow-right"
          color="neutral"
          variant="subtle"
          label="Inspect"
          :disabled="!matchIdInput.trim()"
          @click="submitMatchSearch"
        />
      </div>
    </div>

    <div class="mb-4 flex flex-wrap items-center gap-2">
      <USelect
        v-model="issue"
        :items="issueItems"
        icon="i-lucide-filter"
        placeholder="Issue"
        class="w-52"
      />
      <USelect
        v-model="queue"
        :items="queueItems"
        icon="i-lucide-gamepad-2"
        placeholder="Queue"
        class="w-44"
      />
      <USelect
        v-model="ageWindow"
        :items="ageItems"
        icon="i-lucide-clock"
        placeholder="Age"
        class="w-44"
      />
      <UButton
        v-if="hasActiveFilters"
        icon="i-lucide-x"
        color="neutral"
        variant="ghost"
        label="Clear"
        @click="resetFilters"
      />
      <p class="ms-auto text-xs text-dimmed">
        A missing timeline is only flagged once older than {{ staleHours }}h.
      </p>
    </div>

    <FetchErrorAlert
      v-if="error"
      :error="error"
      title="Failed to load data-quality report"
    />

    <!-- Loading skeleton -->
    <div v-else-if="pending && groups.length === 0" class="space-y-3">
      <USkeleton v-for="n in 3" :key="n" class="h-12 w-full" />
    </div>

    <!-- Empty state -->
    <div v-else-if="groups.length === 0" class="py-12 text-center">
      <UIcon name="i-lucide-shield-check" class="mx-auto mb-3 size-8 text-success/70" />
      <p class="text-sm font-medium text-highlighted">
        No incomplete or inconsistent matches found.
      </p>
      <p class="mt-1 text-xs text-muted">
        Nothing in the scanned window trips the active checks.
      </p>
    </div>

    <!-- One accordion item per flagged issue type: the worst group opens on
         arrival, and only that group's table is mounted — so a single fetch
         runs instead of one per group. -->
    <UCard v-else :ui="{ body: 'py-0' }">
      <UAccordion
        v-model="openGroup"
        :items="groupItems"
        :ui="{ trigger: 'py-3 gap-3', label: 'flex-1 min-w-0', body: 'pb-4' }"
      >
        <template #default="{ item }">
          <span class="flex w-full min-w-0 items-center gap-3">
            <UIcon
              :name="item.meta.icon"
              class="size-4 shrink-0"
              :class="ISSUE_ICON_CLASS[item.meta.color]"
            />
            <span class="shrink-0 text-sm font-medium text-highlighted">
              {{ item.meta.label }}
            </span>
            <span class="hidden min-w-0 truncate text-xs font-normal text-muted sm:block">
              {{ item.meta.description }}
            </span>
            <UBadge
              class="ms-auto shrink-0"
              :color="item.meta.color"
              variant="subtle"
              :label="`${item.group.count.toLocaleString('en-US')} ${pluralize(item.group.count, 'match', 'matches')}`"
            />
          </span>
        </template>

        <template #body="{ item }">
          <DataQualityGroupTable
            :issue-type="item.group.issueType"
            :count="item.group.count"
            :base-filters="baseFilters"
            :page-size="pageSize"
            :meta="DATA_QUALITY_ISSUE_META"
            :queue-label="dataQualityQueueLabel"
            @select="id => emit('inspect', id)"
          />
        </template>
      </UAccordion>
    </UCard>
  </section>
</template>
