<!--
  The dashboard's role card: the site's "Role distribution"
  (web/app/components/profile/ProfilePositionBreakdown.vue) in the ranked
  card's frame, with the site's role names (Mid, ADC, Support). Each role's share of
  the games is a bar under it, and its win rate sits on the data axis beside the
  count.
-->
<script setup lang="ts">
import type { LaneLine } from '~/utils/player-form'
import { getPositionIconUrl } from '#shared/utils/ddragon'
import { winRateTone } from '#common/utils/rate-tone'

const props = defineProps<{ lanes: LaneLine[] }>()

const LABELS: Record<string, string> = { TOP: 'Top', JUNGLE: 'Jungle', MIDDLE: 'Mid', BOTTOM: 'ADC', UTILITY: 'Support' }

const total = computed(() => props.lanes.reduce((sum, lane) => sum + lane.games, 0))
const rows = computed(() => props.lanes
  .filter(lane => lane.games > 0)
  .sort((a, b) => b.games - a.games)
  .map(lane => ({ ...lane, share: lane.games / total.value, winRate: lane.wins / lane.games })))
</script>

<template>
  <section v-if="rows.length" class="surface flex flex-col gap-3 rounded-lg px-4 py-3">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">Roles</h2>
    <ul class="flex flex-col gap-2.5">
      <li v-for="row in rows" :key="row.position" class="flex flex-col gap-1.5">
        <div class="flex items-center gap-2 text-xs">
          <img :src="getPositionIconUrl(row.position)" :alt="LABELS[row.position]" class="size-4">
          <span class="font-medium text-highlighted">{{ LABELS[row.position] ?? row.position }}</span>
          <span class="ml-auto tabular-nums text-dimmed">{{ row.games }} game{{ row.games === 1 ? '' : 's' }}</span>
          <span class="w-9 text-right font-semibold tabular-nums" :class="winRateTone(row.winRate)" :title="`${row.wins}W ${row.games - row.wins}L`">
            {{ Math.round(row.winRate * 100) }}%
          </span>
        </div>
        <div class="h-1 overflow-hidden rounded-full bg-white/6">
          <div class="h-full rounded-full bg-primary/60" :style="{ width: `${Math.round(row.share * 100)}%` }" />
        </div>
      </li>
    </ul>
  </section>
</template>
