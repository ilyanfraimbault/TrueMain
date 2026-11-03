<script setup lang="ts">
// Overview stat cards: the site-wide totals of `GET /api/ops/stats/overview`,
// fetched by the page (its navbar button refreshes them).
//
// The match-side figures are planner estimates (#2301) — exact counts scanned the
// two largest tables and timed the endpoint out — so they read `≈`. Unavailable,
// they read as a dash with the reason, never as zero.
import type { OverviewStats } from '~~/shared/types/ops'
import { computed } from 'vue'
import { formatNumber } from '~~/shared/utils/format'

const props = defineProps<{
  stats: OverviewStats | null | undefined
  pending: boolean
}>()

interface StatCard {
  title: string
  icon: string
  value: string
  hint?: string
}

function formatEstimate(value: number | null | undefined): string {
  return value === null || value === undefined ? formatNumber(value) : `≈ ${formatNumber(value)}`
}

// The reason only stands in for a missing estimate; a measured one keeps its own hint.
function estimateHint(value: number | null | undefined, hint?: string): string | undefined {
  const s = props.stats
  if (!s) return undefined
  if (value === null || value === undefined) {
    return s.estimatesUnknownReason ?? undefined
  }
  return hint
}

// Map the raw totals onto cards. `formatNumber` renders an em dash when a field
// is missing so a partial payload never shows a bare "0".
const cards = computed<StatCard[]>(() => {
  const s = props.stats
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
      value: formatEstimate(s?.totalMatches),
      hint: estimateHint(
        s?.totalMatches,
        s ? `${formatEstimate(s.totalParticipants)} participants` : undefined,
      ),
    },
    {
      title: 'Matches · last 7d',
      icon: 'i-lucide-calendar-clock',
      value: formatEstimate(s?.matchesLast7Days),
      hint: estimateHint(s?.matchesLast7Days),
    },
    {
      title: 'Matches · last 30d',
      icon: 'i-lucide-calendar-range',
      value: formatEstimate(s?.matchesLast30Days),
      hint: estimateHint(s?.matchesLast30Days),
    },
    {
      title: 'Distinct champions',
      icon: 'i-lucide-list',
      value: formatNumber(s?.distinctChampionsWithGames),
      hint: 'with games',
    },
  ]
})
</script>

<template>
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
</template>
