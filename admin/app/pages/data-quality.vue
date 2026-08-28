<script setup lang="ts">
// Data Quality panel. Two halves, in the order an operator needs them (#992):
//
//  1. A verdict — one line answering "is the database healthy right now?".
//  2. The automated detectors (#924), severity-ordered, one line each: what the
//     check is and what it found. Thresholds, source notes and healthy rows sit
//     behind a per-detector expand, because they are reference material, not
//     status, and printing them on every card made five passing checks as loud
//     as five failing ones.
//  3. The flagged matches from `GET /api/ops/data-quality/incomplete-matches`,
//     grouped by issue type in an accordion so one table shows at a time. The
//     queue/age filters and the match-ID search live *in* this section: they
//     have never applied to the detectors, which audit the whole corpus rather
//     than a queue-and-age slice, and sitting in the page header they read as if
//     they did.
//
// Each check is queue-scoped on the backend (lane checks never fire on ARAM), so
// the list only carries genuine problems. Row click (or a match-ID search /
// deep-link via `?match=ID`) opens a slide-over with the two teams laid out by
// position and the missing slots highlighted. Read-only diagnostics — no repair.
import type {
  AggregateFreshnessResponse,
  BadgeColor,
  DataQualityIssueType,
  DetectorStatus,
  IssueMeta,
  MatchDataQualityDetail,
} from '~~/shared/types/ops'
import { formatDateTime } from '~~/shared/utils/format'

// --- Filters -----------------------------------------------------------------
const issue = ref<'all' | DataQualityIssueType>(ALL)
const queue = ref<string>(ALL)
const ageWindow = ref<'all' | '6' | '24' | '72' | '168'>(ALL)
const pageSize = 25

// Issue-type metadata: label, icon, badge color — drives the filter select and
// the group headers/badges so presentation stays consistent across the panel.
// `IssueMeta` / `BadgeColor` live in shared/types/ops so this page and
// DataQualityGroupTable share one definition.
const ISSUE_META: Record<DataQualityIssueType, IssueMeta> = {
  missingTimeline: {
    label: 'Missing timeline',
    icon: 'i-lucide-clock-alert',
    color: 'warning',
    description: 'Timeline not ingested past the staleness window — likely stuck.',
  },
  wrongParticipantCount: {
    label: 'Wrong participant count',
    icon: 'i-lucide-users',
    color: 'error',
    description: 'Participant rows differ from the queue’s expected count.',
  },
  missingTeamPosition: {
    label: 'Missing team position',
    icon: 'i-lucide-map-pin-off',
    color: 'error',
    description: 'A team is missing one of the five Summoner’s Rift lanes.',
  },
  zeroDuration: {
    label: 'Zero duration',
    icon: 'i-lucide-timer-off',
    color: 'warning',
    description: 'Game has no recorded length — usually a remake or ingest glitch.',
  },
  duplicateChampion: {
    label: 'Duplicate champion',
    icon: 'i-lucide-copy',
    color: 'error',
    description: 'The same champion appears twice on one team.',
  },
}
const ISSUE_ORDER: DataQualityIssueType[] = [
  'missingTimeline',
  'wrongParticipantCount',
  'missingTeamPosition',
  'zeroDuration',
  'duplicateChampion',
]

const issueItems = [
  { label: 'All issues', value: ALL },
  ...ISSUE_ORDER.map(type => ({ label: ISSUE_META[type].label, value: type })),
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
const QUEUE_LABELS: Record<number, string> = {
  420: 'Ranked Solo',
  430: 'Normal',
  440: 'Ranked Flex',
  450: 'ARAM',
  700: 'Clash',
}
function queueLabel(queueId: number): string {
  // Queue id 0 isn't a real queue: Riot returns a stub payload (no queue id,
  // zero duration) for games that aborted before stats were recorded, and the
  // ingest stores it verbatim. Name it instead of echoing a bare "Queue 0".
  if (queueId === 0) {
    return 'Unknown queue'
  }
  return QUEUE_LABELS[queueId] ?? `Queue ${queueId}`
}

const ageItems = [
  { label: 'Any age', value: ALL },
  { label: 'Older than 6h', value: '6' },
  { label: 'Older than 24h', value: '24' },
  { label: 'Older than 3 days', value: '72' },
  { label: 'Older than 7 days', value: '168' },
]

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
function resetFilters() {
  issue.value = ALL
  queue.value = ALL
  ageWindow.value = ALL
}

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

// Worst first. `unknown` outranks green for the same reason it does on the
// backend — a summary must not read clean when part of it was never measured —
// but stays below amber: it is a gap in the audit, not a failure.
const STATUS_RANK: Record<DetectorStatus, number> = { red: 0, amber: 1, unknown: 2, green: 3 }
const orderedDetectors = computed(
  () => [...detectors.value].sort((a, b) => STATUS_RANK[a.status] - STATUS_RANK[b.status]),
)
const failingDetectors = computed(() => orderedDetectors.value.filter(d => d.status !== 'green'))
const passingDetectors = computed(() => orderedDetectors.value.filter(d => d.status === 'green'))

// Passing checks are collapsed by default: on a healthy corpus the section is
// one line, and the operator's attention is never spent on the four checks that
// found nothing.
const showPassing = ref(false)
const listedDetectors = computed(
  () => (showPassing.value ? orderedDetectors.value : failingDetectors.value),
)

function plural(count: number, one: string, many: string): string {
  return count === 1 ? one : many
}

/**
 * The single line the panel exists to produce. Worded from the counts so it can
 * never claim clean while something is red, and neutral — not green — when there
 * is nothing to report on.
 */
const verdict = computed<{ tone: 'success' | 'warning' | 'error' | 'neutral', icon: string, title: string }>(() => {
  const counts = { red: 0, amber: 0, unknown: 0, green: 0 }
  for (const detector of detectors.value) {
    counts[detector.status]++
  }

  if (detectors.value.length === 0) {
    return { tone: 'neutral', icon: 'i-lucide-circle-help', title: 'No automated checks reported' }
  }
  if (counts.red > 0) {
    return {
      tone: 'error',
      icon: 'i-lucide-octagon-alert',
      title: `${counts.red} ${plural(counts.red, 'check is', 'checks are')} failing`,
    }
  }
  if (counts.amber > 0) {
    return {
      tone: 'warning',
      icon: 'i-lucide-triangle-alert',
      title: `${counts.amber} ${plural(counts.amber, 'check needs', 'checks need')} attention`,
    }
  }
  if (counts.unknown > 0) {
    return {
      tone: 'neutral',
      icon: 'i-lucide-circle-help',
      title: `${counts.unknown} ${plural(counts.unknown, 'check', 'checks')} could not be measured`,
    }
  }
  return {
    tone: 'success',
    icon: 'i-lucide-shield-check',
    title: `All ${detectors.value.length} checks pass`,
  }
})

const VERDICT_TONE_CLASS: Record<'success' | 'warning' | 'error' | 'neutral', string> = {
  success: 'text-success',
  warning: 'text-warning',
  error: 'text-error',
  neutral: 'text-muted',
}

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

// --- Flagged-match groups ----------------------------------------------------
// Worst first here too, so the group the accordion opens on arrival is the one
// worth opening: hard inconsistencies (error) before soft ones (warning), then
// the biggest group.
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

const groupItems = computed(() => [...groups.value]
  .sort((a, b) => {
    const severity = ISSUE_SEVERITY[ISSUE_META[a.issueType].color]
      - ISSUE_SEVERITY[ISSUE_META[b.issueType].color]
    return severity !== 0 ? severity : b.count - a.count
  })
  .map(group => ({
    value: group.issueType,
    group,
    meta: ISSUE_META[group.issueType],
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

function submitMatchSearch() {
  if (matchIdInput.value.trim()) {
    openDetail(matchIdInput.value)
  }
}

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
      <div v-if="detectorsPending && detectors.length === 0" class="mb-8">
        <USkeleton class="h-12 w-full max-w-md" />
      </div>
      <div v-else-if="!detectorsError" class="mb-8 flex items-start gap-3">
        <UIcon
          :name="verdict.icon"
          class="size-6 shrink-0"
          :class="VERDICT_TONE_CLASS[verdict.tone]"
        />
        <div class="min-w-0">
          <p class="text-base font-medium" :class="VERDICT_TONE_CLASS[verdict.tone]">
            {{ verdict.title }}
          </p>
          <p class="mt-0.5 text-xs text-muted">
            {{ detectors.length }} automated {{ plural(detectors.length, 'check', 'checks') }}<template
              v-if="detectorsData"
            > · evaluated {{ formatDateTime(detectorsData.evaluatedAtUtc) }}</template><template
              v-if="!pending && !error"
            > · {{ total.toLocaleString('en-US') }} flagged {{ plural(total, 'match', 'matches') }}
              {{ hasActiveFilters ? 'under the active filters' : 'in the scanned window' }}</template>
          </p>
        </div>
      </div>

      <!-- 1. Automated detectors: what the database says about itself. -->
      <section class="mb-10">
        <h2 class="mb-3 text-sm font-medium text-highlighted">
          Automated checks
        </h2>

        <FetchErrorAlert
          v-if="detectorsError"
          :error="detectorsError"
          title="Failed to load the automated detectors"
        />

        <div v-else-if="detectorsPending && detectors.length === 0" class="space-y-3">
          <USkeleton v-for="n in 3" :key="n" class="h-14 w-full" />
        </div>

        <UCard v-else :ui="{ body: 'py-2' }">
          <div class="divide-y divide-default">
            <DataQualityDetectorItem
              v-for="detector in listedDetectors"
              :key="detector.key"
              :detector="detector"
              drill-down-label="Per-champion breakdown"
              @drill-down="openFreshness"
            />
          </div>

          <div v-if="passingDetectors.length > 0" class="pt-2" :class="listedDetectors.length > 0 ? 'border-t border-default mt-2' : ''">
            <UButton
              size="xs"
              color="neutral"
              variant="ghost"
              :icon="showPassing ? 'i-lucide-chevron-up' : 'i-lucide-chevron-down'"
              :label="showPassing
                ? 'Hide passing checks'
                : `Show ${passingDetectors.length} passing ${plural(passingDetectors.length, 'check', 'checks')}`"
              @click="void (showPassing = !showPassing)"
            />
          </div>
        </UCard>
      </section>

      <!-- 2. Flagged matches, with the controls that actually govern them. -->
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
                  :label="`${item.group.count.toLocaleString('en-US')} ${plural(item.group.count, 'match', 'matches')}`"
                />
              </span>
            </template>

            <template #body="{ item }">
              <DataQualityGroupTable
                :issue-type="item.group.issueType"
                :count="item.group.count"
                :base-filters="baseFilters"
                :page-size="pageSize"
                :meta="ISSUE_META"
                :queue-label="queueLabel"
                @select="openDetail"
              />
            </template>
          </UAccordion>
        </UCard>
      </section>

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
            :meta="ISSUE_META"
            :queue-label="queueLabel"
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
