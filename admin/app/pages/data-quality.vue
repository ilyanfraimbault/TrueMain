<script setup lang="ts">
// Data Quality panel. Two halves, in the order an operator needs them (#992):
//
//  1. A verdict — one line answering "is the database healthy right now?"
//     (`DataQualityVerdict`).
//  2. The automated detectors (#924), severity-ordered, one line each: what the
//     check is and what it found (`DataQualityDetectorList`). Thresholds, source
//     notes and healthy rows sit behind a per-detector expand, because they are
//     reference material, not status, and printing them on every card made five
//     passing checks as loud as five failing ones.
//  3. The flagged matches from `GET /api/ops/data-quality/incomplete-matches`,
//     grouped by issue type in an accordion so one table shows at a time
//     (`DataQualityFlaggedMatches`). The queue/age filters and the match-ID search
//     live *in* that section: they have never applied to the detectors, which
//     audit the whole corpus rather than a queue-and-age slice, and sitting in the
//     page header they read as if they did.
//
// Each check is queue-scoped on the backend (lane checks never fire on ARAM), so
// the list only carries genuine problems. Row click (or a match-ID search /
// deep-link via `?match=ID`) opens a slide-over with the two teams laid out by
// position and the missing slots highlighted. Read-only diagnostics — no repair.
//
// The page owns every fetch and the filters: the verdict reads both halves, and
// the navbar button refreshes them together.
import type {
  AggregateFreshnessResponse,
  DataQualityIssueType,
  MatchDataQualityDetail,
} from '~~/shared/types/ops'

// --- Filters -----------------------------------------------------------------
const issue = ref<'all' | DataQualityIssueType>(ALL)
const queue = ref<string>(ALL)
const ageWindow = ref<'all' | '6' | '24' | '72' | '168'>(ALL)
const pageSize = 25

// Queue/age filters shared by every group table; each table pins its own `issue`
// and owns its independent page (see DataQualityGroupTable).
const baseFilters = computed(() => ({
  queue: queue.value === ALL ? undefined : Number(queue.value),
  minAgeHours: ageWindow.value === ALL ? undefined : Number(ageWindow.value),
}))

// Overview fetch: discovers which issue groups exist and their full counts under
// the active filters. The per-group rows it returns are unused — each table
// re-fetches its own paged slice — so it stays pinned to page 1.
const overviewFilters = computed(() => ({
  ...baseFilters.value,
  issue: issue.value === ALL ? undefined : issue.value,
  page: 1,
  pageSize,
}))

const hasActiveFilters = computed(
  () => issue.value !== ALL || queue.value !== ALL || ageWindow.value !== ALL,
)

const { data, pending, error, refresh } = useIncompleteMatches(overviewFilters)

const groups = computed(() => data.value?.groups ?? [])
const total = computed(() => data.value?.total ?? 0)
const staleHours = computed(() => data.value?.staleTimelineThresholdHours ?? 6)

// --- Automated detectors (#924) ---------------------------------------------
// Independent of the filters above: the detectors audit the whole corpus, not a
// queue-and-age slice of flagged matches.
const {
  data: detectorsData,
  pending: detectorsPending,
  error: detectorsError,
  refresh: refreshDetectors,
} = useDataQualityDetectors()

const detectors = computed(() => detectorsData.value?.detectors ?? [])

// The per-champion freshness breakdown is the one heavy query, so it loads on an
// explicit click rather than with the panel.
const freshnessOpen = ref(false)
const freshness = ref<AggregateFreshnessResponse | null>(null)
const freshnessPending = ref(false)
const freshnessError = ref<string | null>(null)

async function openFreshness() {
  freshnessOpen.value = true
  if (freshness.value || freshnessPending.value) {
    return
  }
  freshnessPending.value = true
  freshnessError.value = null
  try {
    freshness.value = await getAggregateFreshness()
  }
  catch {
    freshnessError.value = 'Failed to load the per-champion freshness breakdown.'
  }
  finally {
    freshnessPending.value = false
  }
}

function refreshAll() {
  refresh()
  refreshDetectors()
  // Drop the cached breakdown so a reopen re-measures instead of showing ages
  // computed against an older evaluation — but if the slide-over is open right
  // now, re-measure immediately: clearing alone would leave it rendering an
  // empty panel until the operator closed and reopened it.
  freshness.value = null
  if (freshnessOpen.value) {
    void openFreshness()
  }
}

// --- Match-ID search / deep link --------------------------------------------
const matchIdInput = ref('')

// Detail slide-over state (mirrors the open match into `?match=ID`).
const {
  detailOpen,
  detail,
  detailPending,
  detailError,
  detailErrorTraceId,
  detailId,
  openDetail,
} = useDeepLinkedDetail<MatchDataQualityDetail>({
  queryKey: 'match',
  fetch: getMatchDataQuality,
  notFoundMessage: id => `No match found with id "${id}".`,
  loadErrorMessage: 'Failed to load match detail.',
  onDeepLink: (id) => {
    matchIdInput.value = id
  },
})

const detailTitle = computed(() => detail.value?.matchId ?? detailId.value ?? 'Match detail')
</script>

<template>
  <UDashboardPanel id="data-quality">
    <template #header>
      <UDashboardNavbar title="Data Quality" icon="i-lucide-shield-alert">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            icon="i-lucide-refresh-cw"
            color="neutral"
            variant="ghost"
            :loading="pending || detectorsPending"
            aria-label="Refresh"
            @click="refreshAll()"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <!-- The verdict: the one line the panel exists to produce. -->
      <DataQualityVerdict
        :detectors-data="detectorsData"
        :pending="detectorsPending"
        :failed="!!detectorsError"
        :flagged-total="!pending && !error ? total : null"
        :has-active-filters="hasActiveFilters"
      />

      <!-- 1. Automated detectors: what the database says about itself. -->
      <DataQualityDetectorList
        :detectors="detectors"
        :pending="detectorsPending"
        :error="detectorsError"
        @drill-down="openFreshness"
      />

      <!-- 2. Flagged matches, with the controls that actually govern them. -->
      <DataQualityFlaggedMatches
        v-model:issue="issue"
        v-model:queue="queue"
        v-model:age-window="ageWindow"
        v-model:match-id="matchIdInput"
        :groups="groups"
        :pending="pending"
        :error="error"
        :stale-hours="staleHours"
        :has-active-filters="hasActiveFilters"
        :base-filters="baseFilters"
        :page-size="pageSize"
        @inspect="openDetail"
      />

      <!-- Per-match detail slide-over: teams by position, gaps highlighted -->
      <USlideover
        v-model:open="detailOpen"
        :title="detailTitle"
        :ui="{ content: 'sm:max-w-2xl' }"
      >
        <template #body>
          <DataQualityMatchDetail
            :detail="detail"
            :pending="detailPending"
            :error="detailError"
            :error-trace-id="detailErrorTraceId"
            :meta="DATA_QUALITY_ISSUE_META"
            :queue-label="dataQualityQueueLabel"
          />
        </template>
      </USlideover>

      <!-- Per-champion aggregate freshness: the heavy breakdown, loaded on click. -->
      <USlideover
        v-model:open="freshnessOpen"
        title="Aggregate freshness by champion"
        :ui="{ content: 'sm:max-w-xl' }"
      >
        <template #body>
          <DataQualityFreshnessBreakdown
            :freshness="freshness"
            :pending="freshnessPending"
            :error="freshnessError"
          />
        </template>
      </USlideover>
    </template>
  </UDashboardPanel>
</template>
