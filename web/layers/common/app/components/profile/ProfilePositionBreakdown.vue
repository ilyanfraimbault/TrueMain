<script setup lang="ts">
import type { ProfilePositionStat } from '#shared/types/profile'
import { formatPercentage, getPositionIconUrl } from '#shared/utils/ddragon'
import { positionLabel } from '#common/utils/positions'

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
  /** The figures beside the role's icon. Defaults to its game count. */
  games?: (props: { role: ProfilePositionStat }) => unknown
}>()

// Each position under its one name (`positions.ts`), the name the filters and
// the match rows use. Sorted by games desc so the player's main role always
// sits at the top of the list.
const sorted = computed(() =>
  props.positions
    .filter(p => p.games > 0)
    .map(stat => ({ stat, label: positionLabel(stat.position) ?? stat.position }))
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
        class="grid grid-cols-[auto_1fr_auto] items-center gap-3 px-3 py-2"
      >
        <!-- The lane icon alone names the role; its name is the tooltip and the
             alt text, never a second label beside it. -->
        <UTooltip :text="label" :delay-duration="150">
          <SkeletonImage
            :src="getPositionIconUrl(stat.position)"
            :alt="label"
            class="size-5"
            transparent
          />
        </UTooltip>
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
