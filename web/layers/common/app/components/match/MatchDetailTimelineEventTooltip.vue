<script setup lang="ts">
import type { AxisRow } from '#common/utils/timeline-events'
import { formatGameClock } from '#common/utils/win-probability-swing'

/**
 * The tooltip of a dot on the match detail's event axis, in the desktop
 * recap's language (`RecapTimelineEvents.vue`): a kill as its killer's
 * champion, the kill's glyph and its victims' (a multi-kill on one line), an
 * objective as the game's minimap icon of it, each with its game time. It
 * hangs away from the axis on its dot's side — above for the viewer's side,
 * below for the enemy's — and towards the middle near an edge.
 */
const props = defineProps<{
  rows: AxisRow[]
  /** The dot's place across the axis, %. */
  x: number
  ours: boolean
  champion: (participantId: number) => { name: string, iconUrl: string | null }
}>()

const canonicalIcon = useCanonicalIcon()

const shift = computed(() => {
  if (props.x < 15) return 'translateX(-12px)'
  if (props.x > 85) return 'translateX(calc(-100% + 12px))'
  return 'translateX(-50%)'
})

const portrait = (participantId: number) => ({ ...props.champion(participantId), src: canonicalIcon(props.champion(participantId).iconUrl) })
</script>

<template>
  <div
    role="tooltip"
    class="pointer-events-none absolute z-30 w-max min-w-40 rounded-md bg-ink-950/95 px-2 py-1.5 text-xs shadow-lg ring-1 ring-default backdrop-blur"
    :class="ours ? 'bottom-full mb-1.5' : 'top-full mt-1.5'"
    :style="{ left: `${x}%`, transform: shift }"
  >
    <ul class="grid grid-cols-[auto_1fr_auto] items-center gap-x-2 gap-y-1.5">
      <li v-for="(row, index) in rows" :key="index" class="contents whitespace-nowrap">
        <span v-if="row.type === 'kill'" class="flex shrink-0 items-center gap-1">
          <template v-if="row.killerId !== null">
            <img
              v-if="portrait(row.killerId).src"
              :src="portrait(row.killerId).src"
              :alt="portrait(row.killerId).name"
              width="20"
              height="20"
              class="size-5 rounded ring-1"
              :class="row.ours ? 'ring-ally' : 'ring-enemy'"
            >
            <span v-else class="size-5 rounded bg-elevated ring-1" :class="row.ours ? 'ring-ally' : 'ring-enemy'" :title="portrait(row.killerId).name" />
            <UIcon name="i-lucide-swords" class="size-3.5" :class="row.ours ? 'text-ally' : 'text-enemy'" />
          </template>
          <UIcon v-else name="i-lucide-skull" class="size-3.5" :class="row.ours ? 'text-ally' : 'text-enemy'" />
          <template v-for="victim in row.victimIds" :key="victim">
            <img
              v-if="portrait(victim).src"
              :src="portrait(victim).src"
              :alt="portrait(victim).name"
              width="20"
              height="20"
              class="size-5 rounded ring-1"
              :class="row.ours ? 'ring-enemy' : 'ring-ally'"
            >
            <span v-else class="size-5 rounded bg-elevated ring-1" :class="row.ours ? 'ring-enemy' : 'ring-ally'" :title="portrait(victim).name" />
          </template>
        </span>
        <img v-else :src="canonicalIcon(row.icon)" alt="" width="20" height="20" class="size-5 shrink-0 object-contain">
        <span :class="row.ours ? 'text-highlighted' : 'text-enemy'">{{ row.type === 'kill' ? row.label : row.marker.label }}</span>
        <span class="pl-3 text-right tabular-nums text-dimmed">{{ formatGameClock(row.ms) }}</span>
      </li>
    </ul>
  </div>
</template>
