<script setup lang="ts">
import type { CurveMetric, TeamCurves } from '#common/utils/timeline-curves'
import { formatCurveValue, formatTeamDifference } from '#common/utils/timeline-curves'
import { formatGameClock } from '#common/utils/win-probability-swing'

// The curves' hover card (#2102): the frame's game clock and team lead, then
// all ten players at that frame — your side's column first, the enemy's
// second, highest first in each — as a champion portrait and a figure. Gold is
// printed in the gold accent, XP and CS in the plain highlighted tone: the
// accent stands for the currency, not for "a number". The viewer's row carries
// the primary mark their line is drawn in, its only key (#1803 keeps the
// account otherwise unmarked); a pinned player's portrait is ringed, the mark
// of a selection. The card opens on the side of the cursor with more room, so
// it never leaves the plot nor covers the hover rule.
const props = defineProps<{
  /** The hovered frame's position on the plot, 0..100. */
  x: number
  ms: number
  /** The perspective side's total minus the other's at that frame. */
  lead: number
  /** The hovered frame's index into every player's points. */
  index: number
  teams: [TeamCurves, TeamCurves]
  metric: CurveMetric
  selfParticipantId: number | null
  pinnedParticipantId: number | null
  champion: (participantId: number) => { name: string, iconUrl: string | null }
}>()

const columns = computed(() => props.teams.map((team, side) => ({
  teamId: team.teamId,
  ours: side === 0,
  rows: team.players
    .map(player => ({
      participantId: player.participantId,
      value: player.points[props.index]?.value ?? player.end,
      ...props.champion(player.participantId),
      self: player.participantId === props.selfParticipantId,
      pinned: player.participantId === props.pinnedParticipantId,
    }))
    .sort((a, b) => b.value - a.value),
})))

const valueTone = computed(() => (props.metric === 'gold' ? 'text-gold' : 'text-highlighted'))

// Right of the cursor in the plot's left half, left of it in the right half.
const placement = computed(() => (props.x < 50
  ? { left: `${props.x}%`, transform: 'translateX(0.5rem)' }
  : { right: `${100 - props.x}%`, transform: 'translateX(-0.5rem)' }))
</script>

<template>
  <div
    class="pointer-events-none absolute top-0 z-10 flex w-max flex-col gap-1 rounded-md bg-default px-2.5 py-1.5 text-xs shadow-lg ring-1 ring-default"
    :style="placement"
  >
    <span class="tabular-nums">
      <span class="text-muted">{{ formatGameClock(ms) }}</span>
      <span class="ml-1.5 font-semibold text-highlighted">{{ formatTeamDifference(lead, metric) }}</span>
    </span>
    <div class="grid grid-cols-2 gap-x-4">
      <ul
        v-for="column in columns"
        :key="column.teamId"
        class="flex flex-col gap-0.5"
        :aria-label="column.ours ? 'Your team' : 'Enemy team'"
      >
        <li
          v-for="row in column.rows"
          :key="row.participantId"
          class="relative flex items-center gap-1.5"
          :data-participant="row.participantId"
        >
          <span v-if="row.self" class="absolute -left-1.5 h-3 w-0.5 rounded-full bg-primary" data-self-mark />
          <SkeletonImage
            :src="row.iconUrl"
            :alt="row.name"
            class="size-4 shrink-0 rounded-sm"
            :class="row.pinned ? 'ring-1 ring-inverted' : ''"
          />
          <span class="ml-auto tabular-nums" :class="[valueTone, row.self || row.pinned ? 'font-semibold' : '']">
            {{ formatCurveValue(row.value, metric) }}
          </span>
        </li>
      </ul>
    </div>
  </div>
</template>
