<script setup lang="ts">
import type { RankHistoryEntry } from '#shared/types/rank-history'
import { rankChartDateLabel } from '#common/utils/rank-chart'
import { formatLeaguePoints } from '#common/utils/tiers'

// The LP curve's hover card: the snapshot's crest, LP and day.
defineProps<{ entry: RankHistoryEntry }>()
</script>

<template>
  <div class="rounded-md border border-default bg-elevated px-2 py-1.5 text-xs shadow-md">
    <div class="flex items-center gap-1.5">
      <RankIcon :tier="entry.tier" :size="20" />
      <span class="font-semibold tabular-nums text-default">
        {{ formatLeaguePoints(entry.leaguePoints) }}
      </span>
    </div>
    <p class="mt-0.5 text-muted">
      {{ rankChartDateLabel(entry.capturedAtUtc) }}
    </p>
  </div>
</template>
