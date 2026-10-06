<script setup lang="ts">
// Overview card: matches ingested (#1025), from `GET /api/ops/stats/matches-ingested`.
// The pipeline's own throughput, bucketed by RUN date — a different question from
// the matches-over-time card, which buckets by game date and therefore barely moves
// when ingestion stalls. Kept as its own card and its own request so the two can
// never be read as two views of one series. Bars, not an area (#1218): a stall has
// to read as a bucket that dropped to the floor, which is what this card exists for,
// and a filled area slopes into the gap instead.
import type { IngestionTimeGranularity } from '~~/shared/types/ops'
import { formatNumber } from '~~/shared/utils/format'

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
</script>

<template>
  <UCard :ui="{ root: 'overflow-visible' }" class="mb-6">
    <template #header>
      <div class="flex items-start justify-between gap-4">
        <PanelTitle
          variant="label"
          title="Matches ingested"
          subtitle="Pipeline throughput by run date."
          info="By run date rather than game date, so the series answers whether
            ingestion kept up."
        />
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
      <ChartsBarChart
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
</template>
