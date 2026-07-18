<script setup lang="ts">
// Aggregation panel — health and coverage of every aggregation pipeline from
// `GET /api/ops/stats/aggregations`: per-family exact row counts, champion and
// patch coverage, data freshness, the latest recorded run (with its summary),
// and the ingestion backlogs that should sit at zero when the pipeline is
// caught up.
import type { AggregationFamily, AggregationRun } from '~~/shared/types/ops'
import { formatDateTime, formatDuration, formatNumber, formatTimeAgo } from '~~/shared/utils/format'

const { data, pending, error, refresh } = useAggregations()

const families = computed<AggregationFamily[]>(() => data.value?.families ?? [])
const backlog = computed(() => data.value?.backlog ?? null)

const FAMILY_META: Record<string, { title: string, icon: string, hint: string }> = {
  builds: {
    title: 'Builds (patterns)',
    icon: 'i-lucide-hammer',
    hint: 'Per-player build/rune/skill patterns, replace-by-scope over live patches.',
  },
  matchups: {
    title: 'Matchups',
    icon: 'i-lucide-swords',
    hint: 'Champion vs champion win rates per lane, patch and rank.',
  },
  timelineLeads: {
    title: 'Timeline leads',
    icon: 'i-lucide-trending-up',
    hint: 'Gold/CS/XP leads per minute interval, patch and rank.',
  },
  powerspikes: {
    title: 'Powerspikes',
    icon: 'i-lucide-zap',
    hint: 'Incremental per-match folding of gold/damage curves and spike events.',
  },
  mains: {
    title: 'Mains',
    icon: 'i-lucide-user-check',
    hint: 'Per-account champion play rates, main/OTP classification.',
  },
}
function familyMeta(key: string) {
  return FAMILY_META[key] ?? { title: key, icon: 'i-lucide-combine', hint: '' }
}

const totalAggregateRows = computed(() =>
  families.value.reduce((sum, family) => sum + family.totalRows, 0),
)

// Latest write across every family — the "aggregation data is this fresh" tile.
const latestAggregatedAtUtc = computed<string | null>(() => {
  const timestamps = families.value
    .map(family => family.lastAggregatedAtUtc)
    .filter((iso): iso is string => !!iso)
  if (!timestamps.length) {
    return null
  }
  return timestamps.reduce((max, iso) => (iso > max ? iso : max))
})

function runStatusColor(status: string | undefined): 'success' | 'error' | 'warning' | 'info' | 'neutral' {
  switch (status) {
    case 'success':
      return 'success'
    case 'failed':
      return 'error'
    case 'abandoned':
      return 'warning'
    case 'running':
      return 'info'
    default:
      return 'neutral'
  }
}

// The run summary is an arbitrary JSONB object of per-run counters (e.g.
// { matchupRows: 22000, champions: 170 }). Render scalar entries as label:value
// chips and skip nested values — the raw payload lives on the Processes page.
function summaryEntries(run: AggregationRun | null): { label: string, value: string }[] {
  const summary = run?.lastSuccessSummary
  if (!summary || typeof summary !== 'object' || Array.isArray(summary)) {
    return []
  }
  return Object.entries(summary)
    .filter(([, value]) => typeof value === 'number' || typeof value === 'string' || typeof value === 'boolean')
    .map(([key, value]) => ({
      label: key.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase(),
      value: typeof value === 'number' ? formatNumber(value) : String(value),
    }))
}
</script>

<template>
  <UDashboardPanel id="aggregation">
    <template #header>
      <UDashboardNavbar title="Aggregation" icon="i-lucide-combine">
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
        title="Failed to load aggregation stats"
        :description="error.message"
        class="mb-6"
      />

      <!-- Summary tiles -->
      <div class="grid grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
        <UCard>
          <p class="text-xs text-muted uppercase">
            Aggregate rows
          </p>
          <p class="mt-1 text-2xl font-semibold text-highlighted tabular-nums">
            {{ formatNumber(totalAggregateRows) }}
          </p>
          <p class="text-xs text-muted">
            across {{ families.length }} families
          </p>
        </UCard>
        <UCard>
          <p class="text-xs text-muted uppercase">
            Last aggregation
          </p>
          <p class="mt-1 text-2xl font-semibold text-highlighted tabular-nums">
            {{ formatTimeAgo(latestAggregatedAtUtc) }}
          </p>
          <p class="text-xs text-muted tabular-nums">
            {{ formatDateTime(latestAggregatedAtUtc) }}
          </p>
        </UCard>
        <UCard>
          <p class="text-xs text-muted uppercase">
            Pending powerspike matches
          </p>
          <p
            class="mt-1 text-2xl font-semibold tabular-nums"
            :class="(backlog?.pendingPowerspikeMatches ?? 0) > 0 ? 'text-warning' : 'text-highlighted'"
          >
            {{ formatNumber(backlog?.pendingPowerspikeMatches) }}
          </p>
          <p class="text-xs text-muted tabular-nums">
            of {{ formatNumber(backlog?.timelineIngestedMatches) }} timeline-ingested
          </p>
        </UCard>
        <UCard>
          <p class="text-xs text-muted uppercase">
            Pending elo enrichment
          </p>
          <p
            class="mt-1 text-2xl font-semibold tabular-nums"
            :class="(backlog?.pendingEloBracketParticipants ?? 0) > 0 ? 'text-warning' : 'text-highlighted'"
          >
            {{ formatNumber(backlog?.pendingEloBracketParticipants) }}
          </p>
          <p class="text-xs text-muted">
            tracked participants without a bracket
          </p>
        </UCard>
      </div>

      <!-- Family cards -->
      <div v-if="pending && !families.length" class="grid gap-6 lg:grid-cols-2">
        <USkeleton v-for="index in 4" :key="index" class="h-64 w-full" />
      </div>

      <div v-else class="grid gap-6 lg:grid-cols-2">
        <UCard v-for="family in families" :key="family.key">
          <template #header>
            <div class="flex items-start justify-between gap-2">
              <div class="flex items-center gap-2">
                <UIcon :name="familyMeta(family.key).icon" class="size-5 text-primary shrink-0" />
                <div>
                  <p class="text-sm font-medium text-highlighted">
                    {{ familyMeta(family.key).title }}
                  </p>
                  <p class="text-xs text-muted">
                    {{ familyMeta(family.key).hint }}
                  </p>
                </div>
              </div>
              <UBadge
                :color="runStatusColor(family.lastRun?.status)"
                variant="subtle"
                :label="family.lastRun?.status ?? 'never ran'"
              />
            </div>
          </template>

          <div class="grid grid-cols-3 gap-4 mb-4">
            <div>
              <p class="text-xs text-muted uppercase">
                Rows
              </p>
              <p class="mt-0.5 text-lg font-semibold text-highlighted tabular-nums">
                {{ formatNumber(family.totalRows) }}
              </p>
            </div>
            <div>
              <p class="text-xs text-muted uppercase">
                Champions
              </p>
              <p class="mt-0.5 text-lg font-semibold text-highlighted tabular-nums">
                {{ formatNumber(family.distinctChampions) }}
              </p>
            </div>
            <div>
              <p class="text-xs text-muted uppercase">
                Patches
              </p>
              <p class="mt-0.5 text-lg font-semibold text-highlighted tabular-nums">
                {{ family.distinctPatches === null ? '—' : formatNumber(family.distinctPatches) }}
              </p>
            </div>
          </div>

          <div class="flex flex-col gap-1.5 mb-4">
            <div
              v-for="table in family.tables"
              :key="table.table"
              class="flex items-center justify-between text-sm"
            >
              <span class="font-mono text-xs text-muted">{{ table.table }}</span>
              <span class="tabular-nums text-highlighted">{{ formatNumber(table.rows) }}</span>
            </div>
          </div>

          <dl class="grid grid-cols-2 gap-x-4 gap-y-1.5 text-sm">
            <dt class="text-muted">
              Data freshness
            </dt>
            <dd class="text-right tabular-nums text-highlighted" :title="formatDateTime(family.lastAggregatedAtUtc)">
              {{ formatTimeAgo(family.lastAggregatedAtUtc) }}
            </dd>
            <dt class="text-muted">
              Last run
            </dt>
            <dd class="text-right tabular-nums text-highlighted" :title="formatDateTime(family.lastRun?.lastFinishedAtUtc)">
              {{ formatTimeAgo(family.lastRun?.lastFinishedAtUtc) }}
            </dd>
            <dt class="text-muted">
              Last success
            </dt>
            <dd class="text-right tabular-nums text-highlighted" :title="formatDateTime(family.lastRun?.lastSuccessAtUtc)">
              {{ formatTimeAgo(family.lastRun?.lastSuccessAtUtc) }}
            </dd>
            <dt class="text-muted">
              Duration
            </dt>
            <dd class="text-right tabular-nums text-highlighted">
              {{ formatDuration(family.lastRun?.durationMs) }}
            </dd>
          </dl>

          <template v-if="summaryEntries(family.lastRun).length">
            <USeparator class="my-3" />
            <div class="flex flex-wrap gap-1.5">
              <UBadge
                v-for="entry in summaryEntries(family.lastRun)"
                :key="entry.label"
                color="neutral"
                variant="subtle"
                :label="`${entry.label}: ${entry.value}`"
              />
            </div>
          </template>
        </UCard>
      </div>
    </template>
  </UDashboardPanel>
</template>
