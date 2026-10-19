<script setup lang="ts">
// The Scrapers tab of the Processes page (#2119): the ops scripts kept outside the repo
// (persona scraper, OTP seeder) report their runs to the ops API, and this tab shows each
// one's latest run — the phase in flight, every phase's counters — and its history. It
// owns its fetches and exposes `refresh`/`pending` so the page's navbar button drives it,
// like the Riot API tab.
import type { ProcessRun } from '~~/shared/types/ops'
import { SCRAPER_PROCESSES } from '~~/shared/utils/scraper-runs'

const HISTORY = 8

const feeds = SCRAPER_PROCESSES.map(scraper => ({
  scraper,
  ...useProcessRuns({ processName: scraper.name, page: 1, pageSize: HISTORY + 1 }),
}))

// Long runs (hours) are watched while they work: refresh every 30 s while visible.
const { lastUpdatedAt, paused, toggle, refreshNow } = useLiveRefresh(feeds.map(feed => feed.refresh))

const pending = computed(() => feeds.some(feed => feed.pending.value))

const detailOpen = ref(false)
const selectedRun = ref<ProcessRun | null>(null)
function openRun(run: ProcessRun) {
  selectedRun.value = run
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
      :data="feed.data.value"
      :pending="feed.pending.value"
      :error="feed.error.value"
      @open-run="openRun"
    />

    <ProcessesRunDetail v-model:open="detailOpen" :run="selectedRun" />
  </div>
</template>
