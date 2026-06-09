<script setup lang="ts">
// Overview panel — site-wide totals from `GET /api/ops/stats/overview`, a
// "matches over time" histogram from `GET /api/ops/stats/matches-over-time`, and
// a "top 10 champions by games" breakdown from `GET /api/ops/stats/champions`
// (no filters). Everything is real: empty/zero responses render honest zero
// states, never fabricated series.
import type { MatchTimeGranularity } from '~~/shared/types/ops'
import { formatNumber } from '~~/shared/utils/format'

const { data: stats, pending, error, refresh } = useOverviewStats()

// --- Matches over time -------------------------------------------------------
// Histogram of match counts by GAME date at a selectable granularity. The select
// drives a reactive refetch; the x-axis label format follows the granularity.
const granularityItems: { label: string, value: MatchTimeGranularity }[] = [
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
const matchesChartData = computed(() =>
  (matchesOverTime.value ?? []).map(b => ({
    label: formatBucketLabel(b.bucket, granularity.value),
    matches: b.matches,
  })),
)
const matchesChartCategories = {
  matches: { name: 'Matches', color: '#34d399' },
}
// nuxt-charts feeds the numeric tick index for a categorical x-axis; map it back
// to the formatted bucket label. Recomputed so labels track the current data.
const matchesXFormatter = computed(() =>
  indexLabelFormatter(matchesChartData.value, row => row.label),
)
const matchesTotal = computed(() =>
  matchesChartData.value.reduce((sum, b) => sum + (b.matches ?? 0), 0),
)

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

// Bar-chart series for the candidate pipeline. Emerald single series; x maps the
// numeric tick index back to the bucket label.
const candidateChartData = computed(() =>
  candidateBuckets.value.map(b => ({ label: b.label, count: b.count ?? 0 })),
)
const candidateChartCategories = {
  count: { name: 'Candidates', color: '#34d399' },
}
// Wrapped in a computed so the label lookup tracks `candidateChartData`
// instead of closing over its initial (empty) value before stats load.
const candidateXFormatter = computed(() =>
  indexLabelFormatter(candidateChartData.value, row => row.label),
)

// Top 10 champions by games for the bottom chart.
const topChampions = computed(() => {
  const rows = champions.value ?? []
  return rows.slice(0, 10).map(row => ({
    label: nameFor(row.championId),
    games: row.games,
  }))
})
const championChartCategories = {
  games: { name: 'Games', color: '#34d399' },
}
// Recomputed against the current slice so labels track the data.
const championXFormatter = computed(() =>
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
      <UAlert
        v-if="error"
        color="error"
        variant="subtle"
        icon="i-lucide-triangle-alert"
        title="Failed to load overview stats"
        :description="error.message"
        class="mb-6"
      />

      <!-- Matches over time -->
      <UCard :ui="{ root: 'overflow-visible' }" class="mb-6">
        <template #header>
          <div class="flex items-start justify-between gap-4">
            <div>
              <p class="text-xs text-muted uppercase mb-1.5">
                Matches over time
              </p>
              <p class="text-sm text-dimmed">
                New matches by game date.
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

        <UAlert
          v-if="matchesError"
          color="error"
          variant="subtle"
          icon="i-lucide-triangle-alert"
          title="Failed to load matches over time"
          :description="matchesError.message"
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
            :x-num-ticks="Math.min(matchesChartData.length, 12)"
            :x-formatter="matchesXFormatter"
            :y-formatter="formatCount"
            :radius="4"
            hide-legend
          />
          <template #fallback>
            <USkeleton class="h-[260px] w-full" />
          </template>
        </ClientOnly>
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
                :height="180"
                :categories="candidateChartCategories"
                :y-axis="['count']"
                :x-num-ticks="candidateChartData.length"
                :x-formatter="candidateXFormatter"
                :y-formatter="formatCount"
                :radius="4"
                hide-legend
              />
              <template #fallback>
                <USkeleton class="h-[180px] w-full" />
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

          <UAlert
            v-if="championsError"
            color="error"
            variant="subtle"
            icon="i-lucide-triangle-alert"
            title="Failed to load champion stats"
            :description="championsError.message"
          />
          <USkeleton v-else-if="topChampionsLoading" class="h-[220px] w-full" />
          <div
            v-else-if="topChampions.length === 0"
            class="h-[220px] flex items-center justify-center text-sm text-muted"
          >
            No champion games recorded yet.
          </div>
          <ClientOnly v-else>
            <NcBarChart
              :data="topChampions"
              :height="220"
              :categories="championChartCategories"
              :y-axis="['games']"
              :x-num-ticks="topChampions.length"
              :x-formatter="championXFormatter"
              :y-formatter="formatCount"
              :radius="4"
              hide-legend
            />
            <template #fallback>
              <USkeleton class="h-[220px] w-full" />
            </template>
          </ClientOnly>
        </UCard>
      </div>
    </template>
  </UDashboardPanel>
</template>
