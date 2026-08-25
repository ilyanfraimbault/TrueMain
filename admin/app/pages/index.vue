<script setup lang="ts">
// Overview panel — site-wide totals from `GET /api/ops/stats/overview`, a
// "matches over time" histogram from `GET /api/ops/stats/matches-over-time`, and
// a "top 10 champions by games" breakdown from `GET /api/ops/stats/champions`
// (no filters). Everything is real: empty/zero responses render honest zero
// states, never fabricated series.
import type { IngestionTimeGranularity, MatchTimeGranularity } from '~~/shared/types/ops'
import { detectorStatusMeta } from '~~/shared/utils/detector-status'
import { formatNumber, formatTimeAgo } from '~~/shared/utils/format'

const { data: stats, pending, error, refresh } = useOverviewStats()

// --- Health verdict strip (#1031) --------------------------------------------
// One line: the cockpit's rolled-up verdict, linking to /health for the signals
// behind it. The Overview remains the post-login landing page, so this is where
// "is anything on fire?" gets answered without a click.
const {
  data: health,
  pending: healthPending,
  error: healthError,
} = usePipelineHealth()

const healthVerdict = computed(() => detectorStatusMeta(health.value?.status))

// --- Matches over time -------------------------------------------------------
// Histogram of match counts by GAME date at a selectable granularity. The select
// drives a reactive refetch; the x-axis label format follows the granularity.
const granularityItems: { label: string, value: MatchTimeGranularity }[] = [
  { label: 'Day', value: 'day' },
  { label: 'Week', value: 'week' },
  { label: 'Month', value: 'month' },
  { label: 'Year', value: 'year' },
  { label: 'Patch', value: 'patch' },
]
const granularity = ref<MatchTimeGranularity>('month')

const {
  data: matchesOverTime,
  pending: matchesPending,
  error: matchesError,
} = useMatchesOverTime(granularity)

// Map buckets to (label, matches) pairs. Labels are formatted per granularity
// from the ISO bucket (time) or used as-is (patch).
// Drawn as BARS, not an area (#1218): games-per-bucket is a flow, and a `patch`
// granularity makes the x-axis outright categorical rather than continuous.
const matchesChartData = computed(() =>
  (matchesOverTime.value ?? []).map(b => ({
    label: formatBucketLabel(b.bucket, granularity.value),
    matches: b.matches,
  })),
)
const matchesChartCategories = {
  matches: { name: 'Matches', color: CHART_PRIMARY },
}
// nuxt-charts feeds the numeric tick index for a categorical x-axis; map it back
// to the formatted bucket label. Recomputed so labels track the current data.
const matchesXFormatter = computed(() =>
  indexLabelFormatter(matchesChartData.value, row => row.label),
)
const matchesTotal = computed(() =>
  matchesChartData.value.reduce((sum, b) => sum + (b.matches ?? 0), 0),
)

// --- Matches ingested (#1025) ------------------------------------------------
// The pipeline's own throughput, bucketed by RUN date — a different question from
// the chart above, which buckets by game date and therefore barely moves when
// ingestion stalls. Kept as its own card and its own request so the two can never
// be read as two views of one series. Bars, not an area (#1218): a stall has to
// read as a bucket that dropped to the floor, which is what this card exists for,
// and a filled area slopes into the gap instead.
const ingestedGranularityItems: { label: string, value: IngestionTimeGranularity }[] = [
  { label: 'Day', value: 'day' },
  { label: 'Week', value: 'week' },
  { label: 'Month', value: 'month' },
]
const ingestedGranularity = ref<IngestionTimeGranularity>('day')

const ingestedWindowItems = [
  { label: '7 days', value: 7 },
  { label: '30 days', value: 30 },
  { label: '90 days', value: 90 },
]
const ingestedWindowDays = ref(30)

const {
  data: ingested,
  pending: ingestedPending,
  error: ingestedError,
} = useMatchesIngested(ingestedGranularity, ingestedWindowDays)

const ingestedChartData = computed(() =>
  (ingested.value?.buckets ?? []).map(b => ({
    label: formatBucketLabel(b.bucket, ingestedGranularity.value),
    inserted: b.matchesInserted,
  })),
)
const ingestedChartCategories = {
  inserted: { name: 'Inserted', color: CHART_PRIMARY },
}

// The companion counters, as totals rather than as a second area: inserted alone
// cannot tell "nothing left to do" from "ran hard and stored nothing", and those
// are opposite states. A window with runs, no inserts and plenty skipped is the
// second one, and it reads straight off this line.
const ingestedTotals = computed(() => {
  const buckets = ingested.value?.buckets ?? []
  return buckets.reduce(
    (acc, b) => ({
      runs: acc.runs + b.runs,
      skipped: acc.skipped + b.matchesSkipped,
      timelines: acc.timelines + b.timelinesUpdated,
    }),
    { runs: 0, skipped: 0, timelines: 0 },
  )
})
const ingestedXFormatter = computed(() =>
  indexLabelFormatter(ingestedChartData.value, row => row.label),
)
const ingestedTotal = computed(() =>
  ingestedChartData.value.reduce((sum, b) => sum + (b.inserted ?? 0), 0),
)

// The series cannot see past the run-retention TTL, and the requested window can
// exceed it. Say so rather than letting the missing tail read as a stopped
// pipeline — the one misreading this chart exists to prevent.
const ingestedBoundNote = computed(() => {
  const payload = ingested.value
  if (!payload || payload.buckets.length === 0) {
    return null
  }
  return payload.windowDays > payload.retentionDays
    ? `Run history is kept ${payload.retentionDays} days, so the series stops there rather than at the requested ${payload.windowDays}.`
    : null
})

// Top-10 champions by games (the endpoint already returns games-desc), used for
// the bar chart at the bottom. Independent request so a champions-stats error
// doesn't blank the totals above.
const {
  data: champions,
  pending: championsPending,
  error: championsError,
} = useChampionStats()
const { nameFor, pending: staticPending } = useChampionStatic()

interface StatCard {
  title: string
  icon: string
  value: string
  hint?: string
}

// Map the raw totals onto cards. `formatNumber` renders an em dash when a field
// is missing so a partial payload never shows a bare "0".
const cards = computed<StatCard[]>(() => {
  const s = stats.value
  return [
    {
      title: 'Tracked accounts',
      icon: 'i-lucide-users',
      value: formatNumber(s?.trackedAccounts),
    },
    {
      title: 'Total mains',
      icon: 'i-lucide-user-check',
      value: formatNumber(s?.totalMains),
    },
    {
      title: 'Total OTPs',
      icon: 'i-lucide-target',
      value: formatNumber(s?.totalOtps),
    },
    {
      title: 'Champions with mains',
      icon: 'i-lucide-swords',
      value: formatNumber(s?.distinctChampionsWithMains),
      hint: s
        ? `of ${formatNumber(s.distinctChampionsWithGames)} with games`
        : undefined,
    },
    {
      title: 'Total matches',
      icon: 'i-lucide-database',
      value: formatNumber(s?.totalMatches),
      hint: s ? `${formatNumber(s.totalParticipants)} participants` : undefined,
    },
    {
      title: 'Matches · last 7d',
      icon: 'i-lucide-calendar-clock',
      value: formatNumber(s?.matchesLast7Days),
    },
    {
      title: 'Matches · last 30d',
      icon: 'i-lucide-calendar-range',
      value: formatNumber(s?.matchesLast30Days),
    },
    {
      title: 'Distinct champions',
      icon: 'i-lucide-list',
      value: formatNumber(s?.distinctChampionsWithGames),
      hint: 'with games',
    },
  ]
})

// Candidate pipeline buckets as ordered (label, count) pairs. The colors trace
// the New -> Validated/Rejected flow while staying close to the emerald palette.
const candidateBuckets = computed(() => {
  const c = stats.value?.candidatesByStatus
  if (!c) {
    return []
  }
  return [
    { label: 'New', count: c.New, color: 'neutral' as const },
    { label: 'Scored', count: c.Scored, color: 'info' as const },
    { label: 'Queued', count: c.Queued, color: 'warning' as const },
    { label: 'Processing', count: c.Processing, color: 'warning' as const },
    { label: 'Validated', count: c.Validated, color: 'success' as const },
    { label: 'Rejected', count: c.Rejected, color: 'error' as const },
  ]
})

const candidatesTotal = computed(() =>
  candidateBuckets.value.reduce((sum, b) => sum + (b.count ?? 0), 0),
)

// Horizontal-bar series for the candidate pipeline. Emerald single series.
// Rendered horizontally, so the bucket label lives on the LEFT (category) axis:
// `candidateLabelFormatter` looks the label up by bar index for that y-axis.
const candidateChartData = computed(() =>
  candidateBuckets.value.map(b => ({ label: b.label, count: b.count ?? 0 })),
)
const candidateChartCategories = {
  count: { name: 'Candidates', color: CHART_PRIMARY },
}
// Chart grows with the number of bars; the skeleton mirrors it to avoid CLS.
const candidateChartHeight = computed(() =>
  barChartHeight(candidateChartData.value.length, { min: 200, step: 34 }),
)
// Wrapped in a computed so the label lookup tracks `candidateChartData`
// instead of closing over its initial (empty) value before stats load.
const candidateLabelFormatter = computed(() =>
  indexLabelFormatter(candidateChartData.value, row => row.label),
)

// Top-N champions by games for the bottom chart.
const TOP_N = 10
const topChampions = computed(() => {
  const rows = champions.value ?? []
  return rows.slice(0, TOP_N).map(row => ({
    label: nameFor(row.championId),
    games: row.games,
  }))
})
const championChartCategories = {
  games: { name: 'Games', color: CHART_PRIMARY },
}
// Chart grows with the number of bars; the skeleton mirrors it to avoid CLS.
const topChampionsChartHeight = computed(() =>
  barChartHeight(topChampions.value.length, { min: 240, step: 30 }),
)
// Horizontal bars: champion name lives on the LEFT (category) axis, looked up
// by bar index. Recomputed against the current slice so labels track the data.
const championLabelFormatter = computed(() =>
  indexLabelFormatter(topChampions.value, row => row.label),
)

const topChampionsLoading = computed(
  () => championsPending.value || staticPending.value,
)
</script>

<template>
  <UDashboardPanel id="overview">
    <template #header>
      <UDashboardNavbar title="Overview" icon="i-lucide-layout-dashboard">
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
    </template>

    <template #body>
      <FetchErrorAlert
        v-if="error"
        :error="error"
        title="Failed to load overview stats"
        class="mb-6"
      />

      <!-- Health verdict strip (#1031). The Overview stays the landing page, so the
           cockpit's one-line answer is surfaced here rather than making an operator
           navigate to find out whether anything is on fire. Deliberately the verdict and
           nothing else: the tiles and the signals live on /health.

           Its own fetch, so a broken /ops/pipeline-health costs this strip and not the
           panel below it — and says so in place rather than rendering a healthy-looking
           blank. -->
      <NuxtLink
        v-if="healthPending || health || healthError"
        to="/health"
        class="group block mb-6 rounded-lg focus-visible:outline-2 focus-visible:outline-primary"
      >
        <UCard class="transition-colors group-hover:bg-elevated/50">
          <USkeleton v-if="healthPending && !health" class="h-6 w-72" />
          <div v-else class="flex items-center gap-3">
            <UIcon
              :name="healthVerdict.icon"
              class="size-5 shrink-0"
              :class="healthError ? 'text-dimmed' : healthVerdict.text"
            />
            <p class="text-sm grow min-w-0 truncate" :class="healthError ? 'text-dimmed italic' : 'text-highlighted'">
              {{ healthError ? 'Pipeline health could not be loaded.' : health?.headline }}
            </p>
            <span v-if="health && !healthError" class="text-xs text-muted shrink-0">
              {{ formatTimeAgo(health.evaluatedAtUtc) }}
            </span>
            <UIcon
              name="i-lucide-arrow-up-right"
              class="size-4 shrink-0 text-dimmed group-hover:text-muted"
            />
          </div>
        </UCard>
      </NuxtLink>

      <!-- Matches over time -->
      <UCard :ui="{ root: 'overflow-visible' }" class="mb-6">
        <template #header>
          <div class="flex items-start justify-between gap-4">
            <div>
              <p class="text-xs text-muted uppercase mb-1.5">
                Matches over time
              </p>
              <p class="text-sm text-dimmed">
                New matches by game <em>date</em> — when they were played, not when we ingested them.
              </p>
            </div>
            <div class="flex items-center gap-2">
              <UBadge
                v-if="!matchesPending && !matchesError && matchesChartData.length"
                color="neutral"
                variant="subtle"
                :label="`${formatNumber(matchesTotal)} total`"
              />
              <USelect
                v-model="granularity"
                :items="granularityItems"
                class="w-32"
                aria-label="Bucket granularity"
              />
            </div>
          </div>
        </template>

        <FetchErrorAlert
          v-if="matchesError"
          :error="matchesError"
          title="Failed to load matches over time"
        />
        <USkeleton v-else-if="matchesPending" class="h-[260px] w-full" />
        <div
          v-else-if="matchesChartData.length === 0"
          class="h-[260px] flex items-center justify-center text-sm text-muted"
        >
          No matches in range.
        </div>
        <ClientOnly v-else>
          <NcBarChart
            :data="matchesChartData"
            :height="260"
            :categories="matchesChartCategories"
            :y-axis="['matches']"
            :x-num-ticks="Math.min(matchesChartData.length, 8)"
            :x-formatter="matchesXFormatter"
            :y-formatter="formatCount"
            :tooltip-title-formatter="labelTooltipTitle"
            v-bind="timeBarProps()"
          />
          <template #fallback>
            <USkeleton class="h-[260px] w-full" />
          </template>
        </ClientOnly>
      </UCard>

      <!-- Matches ingested (#1025) -->
      <UCard :ui="{ root: 'overflow-visible' }" class="mb-6">
        <template #header>
          <div class="flex items-start justify-between gap-4">
            <div>
              <p class="text-xs text-muted uppercase mb-1.5">
                Matches ingested
              </p>
              <p class="text-sm text-dimmed">
                Pipeline throughput by <em>run</em> date — whether ingestion kept up.
              </p>
            </div>
            <div class="flex items-center gap-2">
              <UBadge
                v-if="!ingestedPending && !ingestedError && ingestedChartData.length"
                color="neutral"
                variant="subtle"
                :label="`${formatNumber(ingestedTotal)} inserted`"
              />
              <USelect
                v-model="ingestedGranularity"
                :items="ingestedGranularityItems"
                class="w-28"
                aria-label="Ingestion bucket granularity"
              />
              <USelect
                v-model="ingestedWindowDays"
                :items="ingestedWindowItems"
                class="w-28"
                aria-label="Ingestion window"
              />
            </div>
          </div>
        </template>

        <FetchErrorAlert
          v-if="ingestedError"
          :error="ingestedError"
          title="Failed to load ingestion throughput"
        />
        <USkeleton v-else-if="ingestedPending" class="h-[260px] w-full" />
        <div
          v-else-if="ingestedChartData.length === 0"
          class="h-[260px] flex items-center justify-center text-sm text-muted"
        >
          No ingestion runs on record in this window.
        </div>
        <template v-else>
          <ClientOnly>
            <NcBarChart
              :data="ingestedChartData"
              :height="260"
              :categories="ingestedChartCategories"
              :y-axis="['inserted']"
              :x-num-ticks="Math.min(ingestedChartData.length, 8)"
              :x-formatter="ingestedXFormatter"
              :y-formatter="formatCount"
              :tooltip-title-formatter="labelTooltipTitle"
              v-bind="timeBarProps()"
            />
            <template #fallback>
              <USkeleton class="h-[260px] w-full" />
            </template>
          </ClientOnly>
          <p class="mt-3 text-xs text-dimmed tabular-nums">
            {{ formatNumber(ingestedTotals.runs) }} runs ·
            {{ formatNumber(ingestedTotals.skipped) }} skipped ·
            {{ formatNumber(ingestedTotals.timelines) }} timelines updated
          </p>
          <p v-if="ingestedBoundNote" class="mt-1 text-xs text-dimmed">
            {{ ingestedBoundNote }}
          </p>
        </template>
      </UCard>

      <!-- Stat cards -->
      <UPageGrid class="lg:grid-cols-4 gap-4 sm:gap-6 lg:gap-px">
        <UPageCard
          v-for="(card, index) in cards"
          :key="index"
          :icon="card.icon"
          :title="card.title"
          variant="subtle"
          :ui="{
            container: 'gap-y-1.5',
            wrapper: 'items-start',
            leading: 'p-2.5 rounded-full bg-primary/10 ring ring-inset ring-primary/25 flex-col',
            title: 'font-normal text-muted text-xs uppercase',
          }"
          class="lg:rounded-none first:rounded-l-lg last:rounded-r-lg"
        >
          <div class="flex flex-col gap-0.5">
            <USkeleton v-if="pending" class="h-8 w-20" />
            <span v-else class="text-2xl font-semibold text-highlighted">
              {{ card.value }}
            </span>
            <span v-if="card.hint && !pending" class="text-xs text-dimmed">
              {{ card.hint }}
            </span>
          </div>
        </UPageCard>
      </UPageGrid>

      <div class="grid grid-cols-1 lg:grid-cols-2 gap-4 sm:gap-6 mt-6">
        <!-- Candidate pipeline breakdown -->
        <UCard :ui="{ root: 'overflow-visible' }">
          <template #header>
            <div class="flex items-center justify-between">
              <div>
                <p class="text-xs text-muted uppercase mb-1.5">
                  Candidate pipeline
                </p>
                <p class="text-sm text-dimmed">
                  Main candidates by status.
                </p>
              </div>
              <UBadge
                v-if="!pending"
                color="neutral"
                variant="subtle"
                :label="`${formatNumber(candidatesTotal)} total`"
              />
            </div>
          </template>

          <USkeleton v-if="pending" class="h-[220px] w-full" />
          <div
            v-else-if="candidatesTotal === 0"
            class="h-[220px] flex items-center justify-center text-sm text-muted"
          >
            No candidates yet.
          </div>
          <div v-else class="space-y-4">
            <div class="flex flex-wrap gap-2">
              <UBadge
                v-for="bucket in candidateBuckets"
                :key="bucket.label"
                :color="bucket.color"
                variant="subtle"
              >
                {{ bucket.label }}: {{ formatNumber(bucket.count) }}
              </UBadge>
            </div>
            <ClientOnly>
              <NcBarChart
                :data="candidateChartData"
                :height="candidateChartHeight"
                :categories="candidateChartCategories"
                :y-axis="['count']"
                :y-num-ticks="candidateChartData.length"
                :x-formatter="formatCount"
                :y-formatter="candidateLabelFormatter"
                :tooltip-title-formatter="labelTooltipTitle"
                v-bind="horizontalBarProps(96)"
              />
              <template #fallback>
                <USkeleton
                  class="w-full"
                  :style="{ height: `${candidateChartHeight}px` }"
                />
              </template>
            </ClientOnly>
          </div>
        </UCard>

        <!-- Top champions by games -->
        <UCard :ui="{ root: 'overflow-visible' }">
          <template #header>
            <div>
              <p class="text-xs text-muted uppercase mb-1.5">
                Top champions by games
              </p>
              <p class="text-sm text-dimmed">
                Most-played across all tracked data.
              </p>
            </div>
          </template>

          <FetchErrorAlert
            v-if="championsError"
            :error="championsError"
            title="Failed to load champion stats"
          />
          <USkeleton v-else-if="topChampionsLoading" class="h-[240px] w-full" />
          <div
            v-else-if="topChampions.length === 0"
            class="h-[240px] flex items-center justify-center text-sm text-muted"
          >
            No champion games recorded yet.
          </div>
          <ClientOnly v-else>
            <NcBarChart
              :data="topChampions"
              :height="topChampionsChartHeight"
              :categories="championChartCategories"
              :y-axis="['games']"
              :y-num-ticks="topChampions.length"
              :x-formatter="formatCount"
              :y-formatter="championLabelFormatter"
              :tooltip-title-formatter="labelTooltipTitle"
              v-bind="horizontalBarProps(120)"
            />
            <template #fallback>
              <USkeleton
                class="w-full"
                :style="{ height: `${topChampionsChartHeight}px` }"
              />
            </template>
          </ClientOnly>
        </UCard>
      </div>
    </template>
  </UDashboardPanel>
</template>
