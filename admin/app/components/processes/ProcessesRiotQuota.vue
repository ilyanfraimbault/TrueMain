<script setup lang="ts">
// Riot quota per routing host and lane duty cycle (#1458), from `GET /api/ops/riot-quota`.
// Mounted by the Riot API tab only once a reading exists, so nothing here falls back to
// a zero standing in for "not measured" (#1426). The coverage line states what the
// figures are measured over, so a window the retention cut short never reads as a trend.
import type { RiotQuota } from '~~/shared/types/ops'
import { formatDateTime } from '~~/shared/utils/format'

const props = defineProps<{ quota: RiotQuota }>()

const regional = computed(() => props.quota.routes.filter(route => route.kind === 'regional'))
const platform = computed(() => props.quota.routes.filter(route => route.kind === 'platform'))
const unknown = computed(() => props.quota.routes.filter(route => route.kind === 'unknown'))

// The retention or the per-host cutover ended inside the selected window: the rates
// divide by what was counted, never by the nominal window.
const truncated = computed(() => props.quota.coverageStartUtc !== props.quota.sinceUtc)
const measured = computed(() => props.quota.coveredHours > 0)

const coveredLabel = computed(() => {
  const hours = props.quota.coveredHours
  return hours >= 48 ? `${(hours / 24).toFixed(1)} days` : `${hours.toFixed(1)} hours`
})

const ROUTE_INFO = 'Riot enforces the app rate limit per routing host, so each host is its own budget. '
  + 'Utilisation = calls over the covered span / what the host\'s binding app limit allows over it (every attempt counts, 429s included). '
  + 'Now = the freshest X-App-Rate-Limit-Count that host returned, on the same window. '
  + 'Active min = share of minutes with at least one call. Click a host for the processes and endpoints spending it.'
</script>

<template>
  <div class="flex flex-col gap-6 mb-6">
    <p class="text-sm text-muted">
      <template v-if="measured">
        Per-host figures cover {{ coveredLabel }} since {{ formatDateTime(quota.coverageStartUtc) }}.
      </template>
      <template v-else>
        No per-host measurement yet: only call rollups split by routing host are counted, and none is stored.
      </template>
      <template v-if="quota.retentionDays !== null">
        Call rollups are kept {{ quota.retentionDays }} days<template v-if="quota.oldestRetainedUtc">
          (oldest: {{ formatDateTime(quota.oldestRetainedUtc) }})</template>.
      </template>
      <span v-if="measured && truncated" class="text-warning">
        That is less than the window: older rollups were not split by host or are past the retention, so rates cover the counted part only.
      </span>
    </p>

    <ProcessesRiotQuotaHosts
      title="Regional hosts"
      :info="`account-v1 and match-v5. ${ROUTE_INFO}`"
      :routes="regional"
    />
    <ProcessesRiotQuotaHosts
      title="Platform hosts"
      :info="`summoner, league and champion-mastery. ${ROUTE_INFO}`"
      :routes="platform"
    />
    <ProcessesRiotQuotaHosts
      v-if="unknown.length"
      title="Unresolved host"
      info="Calls whose routing host could not be read from the request."
      :routes="unknown"
    />

    <ProcessesRiotLaneDuty :lanes="quota.lanes" />
  </div>
</template>
