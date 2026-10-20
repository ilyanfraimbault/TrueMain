<script setup lang="ts">
import type { Moment } from '~/types/recordings'
import type { MomentCast } from '~/utils/recording-moments'
import type { EventCluster, TimelineView } from '~/utils/recording-timeline'
import { formatClock, momentDuel, momentLabel, momentSide, objectiveIconUrl } from '~/utils/recording-moments'
import { viewPercent } from '~/utils/recording-timeline'

/**
 * The game's events above the timeline: one dot per event, blue when it went
 * the player's side's way, red when it went the enemy's — events too close to
 * tell apart on screen share a dot, split in both colours when they went both
 * ways. What they are reads in the tooltip the hovered cluster opens, wherever
 * the pointer is on the track: a kill as its killer's champion, the kill's
 * glyph and its victims', an objective as the game's minimap icon of it (the
 * drake's element). A click on a dot seeks a few seconds before its first event.
 */
const props = defineProps<{
  /** In view, in order. */
  clusters: EventCluster[]
  view: TimelineView
  /** The cluster under the pointer, whose events the tooltip lists. */
  hovered: EventCluster | null
  cast: MomentCast
}>()

const emit = defineEmits<{ moment: [moment: Moment] }>()

const { nameOf, portraitOf } = useChampionStatics()

const SIDE_DOT: Record<EventCluster['side'], string> = {
  ally: 'bg-ally',
  enemy: 'bg-enemy',
  mixed: 'bg-[linear-gradient(90deg,var(--color-ally)_50%,var(--color-enemy)_50%)]',
}
const SIDE_TEXT = { ally: 'text-ally', enemy: 'text-enemy' } as const
const SIDE_RING = { ally: 'ring-ally', enemy: 'ring-enemy' } as const
const OTHER_SIDE = { ally: 'enemy', enemy: 'ally' } as const

const rows = computed(() => (props.hovered?.moments ?? []).map((moment) => {
  const side = momentSide(moment)
  return { moment, side, duel: momentDuel(moment, props.cast), icon: objectiveIconUrl(moment) }
}))

const describe = (cluster: EventCluster) =>
  cluster.moments.map(moment => `${momentLabel(moment)} at ${formatClock(moment.videoMs)}`).join(', ')

/** Near an edge, the tooltip hangs from the dot towards the middle instead of spilling out of the track. */
const tooltipShift = computed(() => {
  if (!props.hovered) return ''
  const at = (props.hovered.ms - props.view.startMs) / props.view.spanMs
  if (at < 0.15) return 'translateX(-12px)'
  if (at > 0.85) return 'translateX(calc(-100% + 12px))'
  return 'translateX(-50%)'
})
</script>

<template>
  <button
    v-for="cluster in clusters"
    :key="cluster.ms"
    type="button"
    class="absolute top-2.5 -translate-1/2 rounded-full ring-2 ring-(--ui-bg) transition-transform"
    :class="[SIDE_DOT[cluster.side], cluster.moments.length > 1 ? 'size-3' : 'size-2.5', cluster === hovered && 'z-10 scale-150']"
    :style="{ left: viewPercent(view, cluster.ms) }"
    :aria-label="describe(cluster)"
    @pointerdown.stop
    @click="emit('moment', cluster.moments[0]!)"
  />

  <div
    v-if="hovered"
    role="tooltip"
    class="pointer-events-none absolute bottom-full z-30 mb-1.5 min-w-40 rounded-md bg-ink-950/95 px-2 py-1.5 text-xs shadow-lg ring-1 ring-default backdrop-blur"
    :style="{ left: viewPercent(view, hovered.ms), transform: tooltipShift }"
  >
    <ul class="grid grid-cols-[auto_1fr_auto] items-center gap-x-2 gap-y-1.5">
      <li v-for="({ moment, side, duel, icon }, index) in rows" :key="index" class="contents whitespace-nowrap">
        <span v-if="duel" class="flex shrink-0 items-center gap-1">
          <img
            v-if="duel.killer && portraitOf(duel.killer)"
            :src="portraitOf(duel.killer)!"
            :alt="nameOf(duel.killer)"
            class="size-5 rounded ring-1"
            :class="SIDE_RING[side]"
          >
          <RecapMomentIcon :kind="moment.kind" class="size-3.5" :class="SIDE_TEXT[side]" />
          <img
            v-for="victim in duel.victims"
            :key="victim"
            :src="portraitOf(victim) ?? undefined"
            :alt="nameOf(victim)"
            class="size-5 rounded ring-1"
            :class="SIDE_RING[OTHER_SIDE[side]]"
          >
        </span>
        <img v-else-if="icon" :src="icon" alt="" class="size-5 shrink-0 object-contain">
        <RecapMomentIcon v-else :kind="moment.kind" class="size-3.5 shrink-0" :class="SIDE_TEXT[side]" />
        <span class="whitespace-nowrap" :class="moment.ally === false ? 'text-enemy' : 'text-highlighted'">{{ momentLabel(moment) }}</span>
        <span class="pl-3 text-right tabular-nums text-dimmed">{{ formatClock(moment.videoMs) }}</span>
      </li>
    </ul>
  </div>
</template>
