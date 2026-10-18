<script setup lang="ts">
import type { CurveMetric, TeamCurves } from '#common/utils/timeline-curves'
import { formatCurveValue } from '#common/utils/timeline-curves'

// One side's end-of-game figures beside the curves (#2102): the team total,
// then each player's with a bar on the scale of the game's highest. A click
// pins that player's line (a second click releases it). The viewer's bar takes
// their line's primary colour, the list's only key to it: no ring, no bolder
// name — the account itself stays unmarked (#1803).
const props = defineProps<{
  team: TeamCurves
  ours: boolean
  metric: CurveMetric
  /** The game's highest end value, the bars' full length. */
  max: number
  selfParticipantId: number | null
  pinnedParticipantId: number | null
  champion: (participantId: number) => { name: string, iconUrl: string | null }
}>()

const emit = defineEmits<{ pin: [participantId: number] }>()

const rows = computed(() => props.team.players.map(player => ({
  ...player,
  ...props.champion(player.participantId),
  self: player.participantId === props.selfParticipantId,
  pinned: player.participantId === props.pinnedParticipantId,
  share: props.max > 0 ? (player.end / props.max) * 100 : 0,
})))

function barTone(self: boolean) {
  if (self) return 'bg-primary'
  return props.ours ? 'bg-ally' : 'bg-enemy'
}
</script>

<template>
  <div class="flex flex-col gap-0.5">
    <div class="flex items-baseline justify-between gap-2 px-1.5 pb-1 text-xs">
      <span class="font-semibold" :class="ours ? 'text-ally' : 'text-enemy'">{{ ours ? 'Your team' : 'Enemy team' }}</span>
      <span class="font-semibold tabular-nums text-highlighted">{{ formatCurveValue(team.total, metric) }}</span>
    </div>
    <button
      v-for="row in rows"
      :key="row.participantId"
      type="button"
      class="flex flex-col gap-1 rounded px-1.5 py-1 text-left transition-colors"
      :class="row.pinned ? 'bg-accented' : 'hover:bg-elevated'"
      :aria-pressed="row.pinned"
      :aria-label="`${row.pinned ? 'Unpin' : 'Pin'} ${row.name}'s line`"
      @click="emit('pin', row.participantId)"
    >
      <span class="flex items-center gap-2 text-xs">
        <SkeletonImage
          :src="row.iconUrl"
          :alt="row.name"
          class="size-5 shrink-0 rounded"
        />
        <span class="min-w-0 flex-1 truncate text-default">{{ row.name }}</span>
        <span class="shrink-0 tabular-nums text-highlighted">{{ formatCurveValue(row.end, metric) }}</span>
      </span>
      <span class="h-1 w-full overflow-hidden rounded-full bg-elevated">
        <span
          class="block h-full rounded-full"
          :class="barTone(row.self)"
          :style="{ width: `${row.share}%`, opacity: row.self || row.pinned ? 1 : 0.6 }"
        />
      </span>
    </button>
  </div>
</template>
