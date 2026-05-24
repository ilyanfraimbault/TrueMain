<script setup lang="ts">
import type { ProfilePositionStat } from '~~/shared/types/profile'
import { getPositionIconUrl } from '~~/shared/utils/ddragon'

const props = defineProps<{
  positions: ProfilePositionStat[]
}>()

// Riot-stored position string → community display label. "MIDDLE" reads
// fine but the rest are friendlier with the labels players use in chat
// (MID / ADC / SUPPORT). Sorted by games desc so the player's main role
// always sits at the top of the list.
const POSITION_LABELS: Record<string, string> = {
  TOP: 'TOP',
  JUNGLE: 'JUNGLE',
  MIDDLE: 'MID',
  BOTTOM: 'ADC',
  UTILITY: 'SUPPORT',
}

interface RoleRow {
  position: string
  label: string
  games: number
  rate: number
}

const sorted = computed<RoleRow[]>(() =>
  props.positions
    .filter(p => p.games > 0)
    .map(p => ({
      position: p.position,
      label: POSITION_LABELS[p.position] ?? p.position,
      games: p.games,
      rate: p.rate,
    }))
    .sort((a, b) => b.games - a.games),
)
</script>

<template>
  <section v-if="sorted.length > 0" class="flex flex-col gap-2">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
      Role distribution
    </h2>
    <ul class="flex flex-col divide-y divide-default/40 overflow-hidden rounded-lg bg-elevated/40">
      <li
        v-for="role in sorted"
        :key="role.position"
        class="grid grid-cols-[auto_1fr_auto_auto] items-center gap-3 px-3 py-2"
      >
        <NuxtImg
          :src="getPositionIconUrl(role.position)"
          :alt="role.label"
          class="size-5"
          width="20"
          height="20"
        />
        <span class="text-sm font-medium">
          {{ role.label }}
        </span>
        <span class="text-xs text-muted tabular-nums">
          {{ role.games }} games
        </span>
        <span class="w-12 text-right text-sm font-semibold tabular-nums text-default">
          {{ Math.round(role.rate * 100) }}%
        </span>
      </li>
    </ul>
  </section>
</template>
