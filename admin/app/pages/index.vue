<script setup lang="ts">
// Overview panel — site-wide totals from `GET /api/ops/stats/overview`, a
// "matches over time" histogram from `GET /api/ops/stats/matches-over-time`, the
// ingestion throughput, and a "top 10 champions by games" breakdown from
// `GET /api/ops/stats/champions` (no filters). Everything is real: empty/zero
// responses render honest zero states, never fabricated series.
//
// The page owns what its navbar button refreshes — the totals and the health
// verdict. The histograms and the top-champions chart own their requests
// (`components/overview/`): they were never part of the manual refresh.

const { data: stats, pending, error, refresh } = useOverviewStats()

// --- Health verdict strip (#1031) --------------------------------------------
// One line: the cockpit's rolled-up verdict, linking to /health for the signals
// behind it. The Overview remains the post-login landing page, so this is where
// "is anything on fire?" gets answered without a click.
const {
  data: fetchedHealth,
  pending: healthPending,
  error: healthError,
  refresh: refreshHealth,
} = usePipelineHealth()

// The last good verdict survives a failed refresh (#1427), see HealthVerdictStrip.
const health = useLastGoodPayload(fetchedHealth, 'pipeline-health')

// The strip answers "is anything on fire?", so it keeps itself current on a 30 s
// timer (#1411). Only the strip: the totals and histograms below move on a
// pipeline cadence measured in hours, and re-drawing their charts every half
// minute would be motion without information.
const {
  lastUpdatedAt,
  paused: livePaused,
  toggle: toggleLive,
  refreshNow,
} = useLiveRefresh(refreshHealth)

// The manual button is the whole panel's refresh, verdict strip included, and it
// restarts the live countdown so a click is never followed by an immediate tick.
async function refreshAll() {
  await Promise.all([refresh(), refreshNow()])
}
</script>

<template>
  <UDashboardPanel id="overview">
    <template #header>
      <UDashboardNavbar title="Overview" icon="i-lucide-layout-dashboard">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <LiveRefreshIndicator
            :last-updated-at="lastUpdatedAt"
            :paused="livePaused"
            @toggle="toggleLive"
          />
          <UButton
            icon="i-lucide-refresh-cw"
            color="neutral"
            variant="ghost"
            :loading="pending"
            aria-label="Refresh"
            @click="refreshAll()"
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

      <HealthVerdictStrip
        :health="health"
        :pending="healthPending"
        :failed="!!healthError"
        class="mb-6"
      />

      <OverviewMatchesOverTime />

      <!-- Matches ingested (#1025) -->
      <OverviewMatchesIngested />

      <OverviewStatCards :stats="stats" :pending="pending" />

      <div class="grid grid-cols-1 lg:grid-cols-2 gap-4 sm:gap-6 mt-6">
        <OverviewCandidatePipeline :stats="stats" :pending="pending" />
        <OverviewTopChampions />
      </div>
    </template>
  </UDashboardPanel>
</template>
