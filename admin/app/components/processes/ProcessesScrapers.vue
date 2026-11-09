<script setup lang="ts">
// The Scrapers tab of the Processes page (#2119): the ops scripts kept outside the repo
// (persona scraper, OTP seeder) report their runs to the ops API, and this tab shows each
// one's latest run — the phase in flight, every phase's counters — and its history. It
// owns its fetches and exposes `refresh`/`pending` so the page's navbar button drives it,
// like the Riot API tab.
import type { ProcessRunReadModel } from '#site-shared/contract/api'
import { SCRAPER_PROCESSES } from '~~/shared/utils/scraper-runs'
import { useLastGoodPayload } from '~/composables/useLastGoodPayload'
import { liveRun } from '~/utils/process-chain'

const HISTORY = 8

const feeds = SCRAPER_PROCESSES.map((scraper) => {
  const feed = useProcessRuns({ processName: scraper.name, page: 1, pageSize: HISTORY + 1 })
  // A failed 30 s refresh keeps the runs on screen with a note, never an alert alone (#2471).
  return { scraper, ...feed, shown: useLastGoodPayload(feed.data, scraper.name) }
})

// Long runs (hours) are watched while they work: refresh every 30 s while visible.
const { lastUpdatedAt, paused, toggle, refreshNow } = useLiveRefresh(feeds.map(feed => feed.refresh))

const pending = computed(() => feeds.some(feed => feed.pending.value))

// The slide-over keeps the clicked run's id and reads the run from the live feeds
// (#2471): a Running run settles in it with the next refresh instead of staying the
// snapshot of the click. The snapshot is only the fallback for a run that left them.
// The selection outlives the close so the panel does not blank while it slides out.
const selected = ref<ProcessRunReadModel | null>(null)
const detailOpen = ref(false)
const selectedRun = computed(() => liveRun(selected.value, feeds.map(feed => feed.shown.value?.runs ?? [])))
function openRun(run: ProcessRunReadModel) {
  selected.value = run
  detailOpen.value = true
}

defineExpose({ refresh: refreshNow, pending })
</script>

<template>
  <div class="space-y-6">
    <div class="flex justify-end">
      <LiveRefreshIndicator :last-updated-at="lastUpdatedAt" :paused="paused" @toggle="toggle" />
    </div>

    <ProcessesScraperCard
      v-for="feed in feeds"
      :key="feed.scraper.name"
      :scraper="feed.scraper"
      :data="feed.shown.value"
      :pending="feed.pending.value"
      :error="feed.error.value"
      @open-run="openRun"
    />

    <ProcessesRunDetail v-model:open="detailOpen" :run="selectedRun" />
  </div>
</template>
