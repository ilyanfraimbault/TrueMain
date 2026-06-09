<script setup lang="ts">
// Overview / landing panel. Stat cards + one example chart wired to SAMPLE
// data so the shell is demonstrably alive. Real numbers come later from the
// ops API via `/api/ops/*` — see the TODOs below.

interface OverviewStat {
  title: string
  icon: string
  value: string
  // Percent change vs the previous window. Positive renders success, negative
  // renders error. Sample only.
  variation: number
}

// TODO(ops): replace with a real summary endpoint, e.g.
//   const { data } = await useFetch('/api/ops/overview')
// and map its fields onto these cards. Placeholder values only.
const stats: OverviewStat[] = [
  { title: 'Tracked mains', icon: 'i-lucide-users', value: '—', variation: 0 },
  { title: 'Matches ingested (24h)', icon: 'i-lucide-swords', value: '—', variation: 0 },
  { title: 'Queue depth', icon: 'i-lucide-list-checks', value: '—', variation: 0 },
  { title: 'Last run', icon: 'i-lucide-clock', value: '—', variation: 0 },
]

// TODO(ops): replace with a real time-series (e.g. matches ingested per day)
// from the ops API. Static sample so the chart renders something meaningful.
interface IngestPoint {
  date: string
  matches: number
}

const chartData: IngestPoint[] = [
  { date: 'Mon', matches: 1240 },
  { date: 'Tue', matches: 1980 },
  { date: 'Wed', matches: 1560 },
  { date: 'Thu', matches: 2210 },
  { date: 'Fri', matches: 1890 },
  { date: 'Sat', matches: 2640 },
  { date: 'Sun', matches: 2310 },
]

// Emerald-400 to stay on the TrueMain palette.
const chartCategories = {
  matches: { name: 'Matches', color: '#34d399' },
}

// x is index-based: the chart feeds the tick's numeric index, which we map
// back to the day label. y is the raw matches count.
const xFormatter = (tick: number | Date) =>
  chartData[Number(tick)]?.date ?? ''
const yFormatter = (tick: number | Date) =>
  Number(tick).toLocaleString('en-US')
</script>

<template>
  <UDashboardPanel id="overview">
    <template #header>
      <UDashboardNavbar title="Overview" icon="i-lucide-layout-dashboard">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>

      <UDashboardToolbar>
        <template #left>
          <!-- TODO(ops): wire a real date-range / period filter here. -->
          <UButton
            icon="i-lucide-calendar"
            color="neutral"
            variant="outline"
            label="Last 7 days"
            disabled
          />
        </template>
      </UDashboardToolbar>
    </template>

    <template #body>
      <UPageGrid class="lg:grid-cols-4 gap-4 sm:gap-6 lg:gap-px">
        <UPageCard
          v-for="(stat, index) in stats"
          :key="index"
          :icon="stat.icon"
          :title="stat.title"
          variant="subtle"
          :ui="{
            container: 'gap-y-1.5',
            wrapper: 'items-start',
            leading: 'p-2.5 rounded-full bg-primary/10 ring ring-inset ring-primary/25 flex-col',
            title: 'font-normal text-muted text-xs uppercase',
          }"
          class="lg:rounded-none first:rounded-l-lg last:rounded-r-lg"
        >
          <div class="flex items-center gap-2">
            <span class="text-2xl font-semibold text-highlighted">
              {{ stat.value }}
            </span>
            <UBadge
              v-if="stat.variation !== 0"
              :color="stat.variation > 0 ? 'success' : 'error'"
              variant="subtle"
              class="text-xs"
            >
              {{ stat.variation > 0 ? '+' : '' }}{{ stat.variation }}%
            </UBadge>
          </div>
        </UPageCard>
      </UPageGrid>

      <UCard class="mt-6" :ui="{ root: 'overflow-visible' }">
        <template #header>
          <div>
            <p class="text-xs text-muted uppercase mb-1.5">
              Matches ingested
            </p>
            <p class="text-sm text-dimmed">
              Sample data — replace with a real ops time-series.
            </p>
          </div>
        </template>

        <!-- TODO(ops): swap `chartData` for a real series and drop ClientOnly
             only if the data is available at SSR time. nuxt-charts renders
             client-side, hence the ClientOnly + skeleton fallback. -->
        <ClientOnly>
          <NcAreaChart
            :data="chartData"
            :height="280"
            :categories="chartCategories"
            :x-num-ticks="chartData.length"
            :x-formatter="xFormatter"
            :y-formatter="yFormatter"
          />
          <template #fallback>
            <USkeleton class="h-[280px] w-full" />
          </template>
        </ClientOnly>
      </UCard>
    </template>
  </UDashboardPanel>
</template>
