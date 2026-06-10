<script setup lang="ts">
// Data Quality panel — surfaces matches with incomplete/inconsistent data from
// `GET /api/ops/data-quality/incomplete-matches`, grouped by issue type. Each
// check is queue-scoped on the backend (lane checks never fire on ARAM), so the
// list only carries genuine problems. Row click (or a match-ID search /
// deep-link via `?match=ID`) opens a slide-over with the two teams laid out by
// position and the missing slots highlighted. Read-only diagnostics — no repair.
import type { TableColumn } from '@nuxt/ui'
import type {
  DataQualityIssueType,
  FlaggedMatch,
  MatchDataQualityDetail,
} from '~~/shared/types/ops'
import { formatDateTime, formatDuration } from '~~/shared/utils/format'

const route = useRoute()
const router = useRouter()

const { nameFor, iconFor } = useChampionStatic()

// --- Filters -----------------------------------------------------------------
// Reka UI forbids an empty-string SelectItem value, so "All …" uses the
// non-empty `'all'` sentinel; `filters` maps it back to `undefined`.
const ALL = 'all'

const issue = ref<'all' | DataQualityIssueType>(ALL)
const queue = ref<string>(ALL)
const ageWindow = ref<'all' | '6' | '24' | '72' | '168'>(ALL)
const page = ref(1)
const pageSize = 25

// Issue-type metadata: label, icon, badge color — drives the filter select and
// the group headers/badges so presentation stays consistent across the panel.
type BadgeColor = 'error' | 'warning' | 'info' | 'neutral'
interface IssueMeta {
  label: string
  icon: string
  color: BadgeColor
  description: string
}
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
  return QUEUE_LABELS[queueId] ?? `Queue ${queueId}`
}

const ageItems = [
  { label: 'Any age', value: ALL },
  { label: 'Older than 6h', value: '6' },
  { label: 'Older than 24h', value: '24' },
  { label: 'Older than 3 days', value: '72' },
  { label: 'Older than 7 days', value: '168' },
]

const filters = computed(() => ({
  issue: issue.value === ALL ? undefined : issue.value,
  queue: queue.value === ALL ? undefined : Number(queue.value),
  minAgeHours: ageWindow.value === ALL ? undefined : Number(ageWindow.value),
  page: page.value,
  pageSize,
}))

const hasActiveFilters = computed(() =>
  Boolean(issue.value !== ALL || queue.value !== ALL || ageWindow.value !== ALL),
)
function resetFilters() {
  issue.value = ALL
  queue.value = ALL
  ageWindow.value = ALL
}

// Any filter change resets to the first page so a narrower filter can't strand
// us on a now-out-of-range page.
watch([issue, queue, ageWindow], () => {
  page.value = 1
})

const { data, pending, error, refresh } = useIncompleteMatches(filters)

const groups = computed(() => data.value?.groups ?? [])
const total = computed(() => data.value?.total ?? 0)
const staleHours = computed(() => data.value?.staleTimelineThresholdHours ?? 6)

function issueMeta(type: DataQualityIssueType): IssueMeta {
  return ISSUE_META[type]
}

// --- Match-ID search / deep link --------------------------------------------
const matchIdInput = ref('')

// Detail slide-over state.
const detailOpen = ref(false)
const detail = ref<MatchDataQualityDetail | null>(null)
const detailPending = ref(false)
const detailError = ref<string | null>(null)
const detailId = ref<string | null>(null)

async function openDetail(matchId: string) {
  const id = matchId.trim()
  if (!id) {
    return
  }
  detailId.value = id
  detailOpen.value = true
  detailPending.value = true
  detailError.value = null
  detail.value = null
  // Reflect the open match in the URL so the view is deep-linkable / shareable.
  if (route.query.match !== id) {
    router.replace({ query: { ...route.query, match: id } })
  }
  try {
    detail.value = await getMatchDataQuality(id)
  }
  catch (err: unknown) {
    const e = err as { statusCode?: number, data?: { message?: string }, message?: string }
    detailError.value = e?.statusCode === 404
      ? `No match found with id "${id}".`
      : (e?.data?.message ?? e?.message ?? 'Failed to load match detail.')
  }
  finally {
    detailPending.value = false
  }
}

function submitMatchSearch() {
  if (matchIdInput.value.trim()) {
    openDetail(matchIdInput.value)
  }
}

// Drop the `?match=` query when the slide-over closes so a refresh doesn't
// re-open it unexpectedly.
watch(detailOpen, (open) => {
  if (!open && route.query.match) {
    const { match: _match, ...rest } = route.query
    router.replace({ query: rest })
  }
})

// Deep-link: open the slide-over on initial load when `?match=ID` is present.
onMounted(() => {
  const fromQuery = route.query.match
  const id = Array.isArray(fromQuery) ? fromQuery[0] : fromQuery
  if (id) {
    matchIdInput.value = id
    openDetail(id)
  }
})

// --- Flagged-match table -----------------------------------------------------
const columns: TableColumn<FlaggedMatch>[] = [
  { accessorKey: 'matchId', header: 'Match' },
  { accessorKey: 'platformId', header: 'Region' },
  { accessorKey: 'queueId', header: 'Queue' },
  { accessorKey: 'gameStartTimeUtc', header: 'Played' },
  { accessorKey: 'participantCount', header: 'Players' },
  { accessorKey: 'issues', header: 'Also flagged' },
]

// --- Detail layout helpers ---------------------------------------------------
const detailTitle = computed(() => detail.value?.matchId ?? detailId.value ?? 'Match detail')

// Win/loss tint for a team header (teams are tinted, not the gaps).
function teamAccent(win: boolean | null | undefined): string {
  if (win === true) {
    return 'text-success'
  }
  if (win === false) {
    return 'text-error'
  }
  return 'text-muted'
}
function teamLabel(teamId: number, index: number): string {
  if (teamId === 100) {
    return 'Blue team'
  }
  if (teamId === 200) {
    return 'Red team'
  }
  return `Team ${index + 1}`
}
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
            :loading="pending"
            aria-label="Refresh"
            @click="refresh()"
          />
        </template>
      </UDashboardNavbar>

      <UDashboardToolbar>
        <template #left>
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
        </template>
        <template #right>
          <UButton
            v-if="hasActiveFilters"
            icon="i-lucide-x"
            color="neutral"
            variant="ghost"
            label="Clear"
            @click="resetFilters"
          />
        </template>
      </UDashboardToolbar>
    </template>

    <template #body>
      <UAlert
        v-if="error"
        color="error"
        variant="subtle"
        icon="i-lucide-triangle-alert"
        title="Failed to load data-quality report"
        :description="error.message"
        class="mb-6"
      />

      <!-- Match-ID lookup / deep-link -->
      <div class="flex flex-wrap items-center gap-3 mb-6">
        <UInput
          v-model="matchIdInput"
          icon="i-lucide-search"
          placeholder="Inspect a match by ID (e.g. EUW1_1234567890)"
          class="w-full sm:w-96 font-mono"
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

      <!-- Summary -->
      <div class="flex flex-wrap items-center gap-2 mb-5">
        <UBadge
          v-if="!pending"
          :color="total === 0 ? 'success' : 'neutral'"
          variant="subtle"
          :icon="total === 0 ? 'i-lucide-circle-check' : 'i-lucide-flag'"
          :label="total === 0
            ? 'No issues in the scanned window'
            : `${total.toLocaleString('en-US')} flagged ${total === 1 ? 'match' : 'matches'}`"
        />
        <span class="text-xs text-dimmed">
          A missing timeline is only flagged once older than {{ staleHours }}h.
        </span>
      </div>

      <!-- Empty state -->
      <div
        v-if="!pending && groups.length === 0 && !error"
        class="py-16 text-center"
      >
        <UIcon name="i-lucide-shield-check" class="size-10 text-success/70 mx-auto mb-3" />
        <p class="text-sm text-highlighted font-medium">
          No incomplete or inconsistent matches found.
        </p>
        <p class="text-xs text-muted mt-1">
          Nothing in the scanned window trips the active checks.
        </p>
      </div>

      <!-- Loading skeleton -->
      <div v-else-if="pending && groups.length === 0" class="space-y-4">
        <USkeleton v-for="n in 3" :key="n" class="h-40 w-full" />
      </div>

      <!-- One card per flagged issue type -->
      <div v-else class="space-y-6">
        <UCard
          v-for="group in groups"
          :key="group.issueType"
          :ui="{ body: 'p-0 sm:p-0' }"
        >
          <template #header>
            <div class="flex items-center justify-between gap-3">
              <div class="flex items-center gap-2.5 min-w-0">
                <UIcon
                  :name="issueMeta(group.issueType).icon"
                  class="size-5 shrink-0"
                  :class="{
                    'text-error': issueMeta(group.issueType).color === 'error',
                    'text-warning': issueMeta(group.issueType).color === 'warning',
                    'text-info': issueMeta(group.issueType).color === 'info',
                  }"
                />
                <div class="min-w-0">
                  <p class="text-sm font-medium text-highlighted">
                    {{ issueMeta(group.issueType).label }}
                  </p>
                  <p class="text-xs text-muted line-clamp-1">
                    {{ issueMeta(group.issueType).description }}
                  </p>
                </div>
              </div>
              <UBadge
                :color="issueMeta(group.issueType).color"
                variant="subtle"
                :label="`${group.count.toLocaleString('en-US')} ${group.count === 1 ? 'match' : 'matches'}`"
              />
            </div>
          </template>

          <UTable
            :data="group.matches"
            :columns="columns"
            :ui="{ td: 'py-2', tr: 'cursor-pointer' }"
            class="max-h-[420px]"
            sticky
            @select="(_event, row) => openDetail(row.original.matchId)"
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
                  v-for="other in row.original.issues.filter(i => i !== group.issueType)"
                  :key="other"
                  :color="issueMeta(other).color"
                  variant="soft"
                  size="sm"
                  :icon="issueMeta(other).icon"
                  :label="issueMeta(other).label"
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
        </UCard>
      </div>

      <!-- Per-issue pagination: page applies to every group's sample. -->
      <div
        v-if="groups.some(g => g.count > pageSize)"
        class="flex items-center justify-between gap-2 mt-6"
      >
        <p class="text-xs text-muted">
          Each issue shows up to {{ pageSize }} matches per page.
        </p>
        <UPagination
          v-model:page="page"
          :total="Math.max(...groups.map(g => g.count), 0)"
          :items-per-page="pageSize"
          :sibling-count="1"
          active-color="primary"
          variant="subtle"
          :disabled="pending"
        />
      </div>

      <!-- Per-match detail slide-over: teams by position, gaps highlighted -->
      <USlideover
        v-model:open="detailOpen"
        :title="detailTitle"
        :ui="{ content: 'sm:max-w-2xl' }"
      >
        <template #body>
          <div v-if="detailPending" class="space-y-4">
            <USkeleton class="h-16 w-full" />
            <USkeleton class="h-64 w-full" />
          </div>

          <UAlert
            v-else-if="detailError"
            color="error"
            variant="subtle"
            icon="i-lucide-triangle-alert"
            title="Could not load match"
            :description="detailError"
          />

          <div v-else-if="detail" class="space-y-5">
            <!-- Header facts -->
            <dl class="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Region</dt>
                <dd class="font-mono text-xs">{{ detail.platformId }}</dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Queue</dt>
                <dd>
                  {{ queueLabel(detail.queueId) }}
                  <span class="text-dimmed">({{ detail.queueId }})</span>
                </dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Played</dt>
                <dd class="tabular-nums text-xs">{{ formatDateTime(detail.gameStartTimeUtc) }}</dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Duration</dt>
                <dd
                  class="tabular-nums text-xs"
                  :class="detail.gameDurationSeconds <= 0 ? 'text-error font-medium' : ''"
                >
                  {{ detail.gameDurationSeconds <= 0
                    ? '0 (no length recorded)'
                    : formatDuration(detail.gameDurationSeconds * 1000) }}
                </dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Players</dt>
                <dd
                  class="tabular-nums text-xs"
                  :class="detail.expectedParticipantCount !== null
                    && detail.participantCount !== detail.expectedParticipantCount
                    ? 'text-error font-medium'
                    : ''"
                >
                  {{ detail.participantCount }}<template
                    v-if="detail.expectedParticipantCount !== null"
                  >&nbsp;/&nbsp;{{ detail.expectedParticipantCount }} expected</template>
                </dd>
              </div>
              <div>
                <dt class="text-muted text-xs uppercase mb-0.5">Timeline</dt>
                <dd>
                  <UBadge
                    :color="detail.timelineIngested ? 'success' : 'warning'"
                    variant="subtle"
                    size="sm"
                    :icon="detail.timelineIngested ? 'i-lucide-check' : 'i-lucide-clock-alert'"
                    :label="detail.timelineIngested ? 'Ingested' : 'Missing'"
                  />
                </dd>
              </div>
            </dl>

            <!-- Flagged issues for this match -->
            <div v-if="detail.issues.length > 0">
              <p class="text-muted text-xs uppercase mb-1.5">Flagged issues</p>
              <div class="flex flex-wrap gap-1.5">
                <UBadge
                  v-for="type in detail.issues"
                  :key="type"
                  :color="issueMeta(type).color"
                  variant="subtle"
                  size="sm"
                  :icon="issueMeta(type).icon"
                  :label="issueMeta(type).label"
                />
              </div>
            </div>
            <UAlert
              v-else
              color="success"
              variant="subtle"
              icon="i-lucide-circle-check"
              title="No issues"
              description="This match passes every applicable check."
            />

            <!-- Teams laid out by position -->
            <div v-if="detail.teams.length > 0" class="space-y-4">
              <p class="text-muted text-xs uppercase">
                {{ detail.hasLanes ? 'Teams by position' : 'Teams' }}
              </p>
              <div
                v-for="(team, teamIndex) in detail.teams"
                :key="team.teamId"
                class="rounded-lg border border-default overflow-hidden"
              >
                <div class="flex items-center justify-between px-3 py-2 bg-elevated/30">
                  <p class="text-xs font-medium" :class="teamAccent(team.slots.find(s => s.win !== null)?.win)">
                    {{ teamLabel(team.teamId, teamIndex) }}
                    <span class="text-dimmed font-normal">· team {{ team.teamId }}</span>
                  </p>
                  <span class="text-xs text-muted">
                    {{ team.slots.filter(s => s.filled).length }} player(s)
                  </span>
                </div>
                <ul class="divide-y divide-default">
                  <li
                    v-for="(slot, slotIndex) in team.slots"
                    :key="`${slot.position}-${slot.participantId ?? slotIndex}`"
                    class="flex items-center gap-3 px-3 py-2"
                    :class="!slot.filled ? 'bg-error/5' : slot.duplicateChampion ? 'bg-warning/5' : ''"
                  >
                    <!-- Position label (lane queues) -->
                    <div
                      v-if="detail.hasLanes"
                      class="w-20 shrink-0 text-xs font-medium uppercase"
                      :class="slot.filled ? 'text-muted' : 'text-error'"
                    >
                      {{ slot.position || 'UNKNOWN' }}
                    </div>

                    <!-- Filled slot: champion + summoner -->
                    <template v-if="slot.filled">
                      <NuxtImg
                        v-if="slot.championId !== null && iconFor(slot.championId)"
                        :src="iconFor(slot.championId)!"
                        :alt="nameFor(slot.championId)"
                        width="24"
                        height="24"
                        loading="lazy"
                        class="size-6 rounded ring-1 ring-default shrink-0"
                      />
                      <div
                        v-else
                        class="size-6 rounded bg-elevated ring-1 ring-default shrink-0"
                      />
                      <div class="min-w-0 flex-1">
                        <p class="text-xs text-highlighted truncate">
                          {{ slot.championId !== null ? nameFor(slot.championId) : '—' }}
                        </p>
                        <p class="text-xs text-muted truncate">
                          {{ slot.summonerName || '—' }}
                        </p>
                      </div>
                      <UBadge
                        v-if="slot.duplicateChampion"
                        color="warning"
                        variant="subtle"
                        size="sm"
                        icon="i-lucide-copy"
                        label="Duplicate"
                      />
                    </template>

                    <!-- Empty slot: a highlighted gap -->
                    <template v-else>
                      <UIcon name="i-lucide-circle-slash" class="size-6 text-error/60 shrink-0" />
                      <span class="text-xs text-error italic">Missing</span>
                    </template>
                  </li>
                </ul>
              </div>
            </div>
            <p v-else class="text-sm text-muted">
              No participant rows recorded for this match.
            </p>
          </div>
        </template>
      </USlideover>
    </template>
  </UDashboardPanel>
</template>
