<script setup lang="ts">
// Overview card: histogram of match counts by GAME date at a selectable
// granularity, from `GET /api/ops/stats/matches-over-time`. The select drives a
// reactive refetch; the x-axis label format follows the granularity. Not part of
// the navbar refresh: the series moves on a pipeline cadence measured in hours.
import type { MatchTimeGranularity } from '~~/shared/types/ops'
import { formatNumber } from '~~/shared/utils/format'

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
</script>

<template>
  <UCard :ui="{ root: 'overflow-visible' }" class="mb-6">
    <template #header>
      <div class="flex items-start justify-between gap-4">
        <PanelTitle
          variant="label"
          title="Matches over time"
          subtitle="New matches by game date."
          info="By game date — when the matches were played, not when we ingested
            them."
        />
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
    <ChartsBarChart
      v-else
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
  </UCard>
</template>
