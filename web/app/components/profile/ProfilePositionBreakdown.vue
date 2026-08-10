<script setup lang="ts">
import type { ProfilePositionStat } from '~~/shared/types/profile'
import { formatPercentage, getPositionIconUrl } from '~~/shared/utils/ddragon'

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

// Plain <img> + a URL built here instead of <NuxtImg> — same `_ipx/…` URL,
// minus the responsive srcset machinery a fixed 20px icon never needed. See
// SkeletonImage.vue for the profiling rationale. The URL comes from the shared
// helper so the glyph shares one cache entry across every size it is shown at.
const canonicalIcon = useCanonicalIcon()
</script>

<template>
  <section v-if="sorted.length > 0" class="flex flex-col gap-2">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
      Role distribution
    </h2>
    <!-- No `overflow-hidden`: the rows have no edge background to clip. It used
         to be forbidden here anyway — `glass`'s backdrop-filter made WebKit clip
         the blur past the radius — but `surface` is opaque and carries no such
         constraint. -->
    <ul class="flex flex-col divide-y divide-default/40 surface rounded-lg">
      <li
        v-for="role in sorted"
        :key="role.position"
        class="grid grid-cols-[auto_1fr_auto_auto] items-center gap-3 px-3 py-2"
      >
        <img
          :src="canonicalIcon(getPositionIconUrl(role.position))"
          :alt="role.label"
          class="size-5"
          width="20"
          height="20"
        >
        <span class="text-sm font-medium">
          {{ role.label }}
        </span>
        <span class="text-xs text-muted tabular-nums">
          {{ role.games }} games
        </span>
        <span class="w-12 text-right text-sm font-semibold tabular-nums text-default">
          {{ formatPercentage(role.rate, 0) }}
        </span>
      </li>
    </ul>
  </section>
</template>
