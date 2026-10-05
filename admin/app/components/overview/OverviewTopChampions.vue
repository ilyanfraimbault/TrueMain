<script setup lang="ts">
// Overview card: top-10 champions by games, from `GET /api/ops/stats/champions`
// (no filters; the endpoint already returns games-desc). Its own request so a
// champions-stats error doesn't blank the totals above.

const {
  data: champions,
  pending: championsPending,
  error: championsError,
} = useChampionStats()
const { nameFor, pending: staticPending } = useChampionStatic()

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
  <UCard :ui="{ root: 'overflow-visible' }">
    <template #header>
      <PanelTitle
        variant="label"
        title="Top champions by games"
        subtitle="Most-played across all tracked data."
      />
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
    <ChartsBarChart
      v-else
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
  </UCard>
</template>
