<script setup lang="ts">
import type { ProfilePositionStat } from '#shared/types/profile'
import { formatPercentage, getPositionIconUrl } from '#shared/utils/ddragon'

// Each role's share of the player's games, main role first. The app's
// dashboard adds how the player fares on each through the `games` slot
// (`DashboardRolesCard`).
const props = withDefaults(defineProps<{
  positions: ProfilePositionStat[]
  title?: string
}>(), {
  title: 'Role distribution',
})

defineSlots<{
  /** The figures beside the role's name. Defaults to its game count. */
  games?: (props: { role: ProfilePositionStat }) => unknown
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

const sorted = computed(() =>
  props.positions
    .filter(p => p.games > 0)
    .map(stat => ({ stat, label: POSITION_LABELS[stat.position] ?? stat.position }))
    .sort((a, b) => b.stat.games - a.stat.games),
)
</script>

<template>
  <section v-if="sorted.length > 0" class="flex flex-col gap-2">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
      {{ title }}
    </h2>
    <!-- No `overflow-hidden`: the rows have no edge background to clip. It used
         to be forbidden here anyway — `glass`'s backdrop-filter made WebKit clip
         the blur past the radius — but `surface` is opaque and carries no such
         constraint. -->
    <ul class="flex flex-col divide-y divide-default/40 surface rounded-lg">
      <li
        v-for="{ stat, label } in sorted"
        :key="stat.position"
        class="grid grid-cols-[auto_1fr_auto_auto] items-center gap-3 px-3 py-2"
      >
        <SkeletonImage
          :src="getPositionIconUrl(stat.position)"
          :alt="label"
          class="size-5"
          transparent
        />
        <span class="text-sm font-medium">
          {{ label }}
        </span>
        <span class="text-xs text-muted tabular-nums">
          <slot name="games" :role="stat">{{ stat.games }} games</slot>
        </span>
        <span class="w-12 text-right text-sm font-semibold tabular-nums text-default">
          {{ formatPercentage(stat.rate, 0) }}
        </span>
      </li>
    </ul>
  </section>
</template>
