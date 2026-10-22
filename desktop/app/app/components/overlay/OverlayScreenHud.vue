<script setup lang="ts">
import type { HudSettings } from '~/types/overlay'
import type { Size } from '~/utils/overlay-layout'
import { SCOREBOARD_ROWS, scoreboardRows } from '~/utils/scoreboard-geometry'

/**
 * The game's own HUD on a copy of the screen, so the panels can keep clear of
 * it: the minimap, the ability bar and, given a `scoreboard`, the TAB
 * scoreboard's rows where the lane-gold plates sit (#2186) — at the player's
 * HUD scale, the nudge left out: the outline is where the game draws them.
 */
const props = defineProps<{ screen?: Size, scoreboard?: Pick<HudSettings, 'globalScale'> | null }>()

const board = computed(() => {
  if (!props.screen || !props.scoreboard) return null
  const { width, height } = props.screen
  const rows = scoreboardRows(props.screen, props.scoreboard)
  const pct = (value: number, of: number) => `${(value / of) * 100}%`
  return {
    left: pct(rows.centreX - rows.width / 2, width),
    top: pct(rows.top, height),
    width: pct(rows.width, width),
    height: pct(rows.pitch * SCOREBOARD_ROWS, height),
  }
})
</script>

<template>
  <div class="pointer-events-none absolute bottom-[1.5%] right-[1%] aspect-square w-[14%] rounded border border-dashed border-default/80" />
  <div class="pointer-events-none absolute bottom-[1%] left-1/2 h-[12%] w-[30%] -translate-x-1/2 rounded border border-dashed border-default/80" />
  <span class="pointer-events-none absolute bottom-[3%] right-[3%] text-[10px] text-dimmed">Minimap</span>
  <span class="pointer-events-none absolute bottom-[5%] left-1/2 -translate-x-1/2 text-[10px] text-dimmed">Abilities and items</span>
  <div
    v-if="board"
    class="pointer-events-none absolute flex flex-col rounded border border-dashed border-accented bg-accented/20"
    :style="board"
    data-scoreboard
  >
    <span class="absolute -top-4 left-0 text-[10px] text-dimmed">Scoreboard (TAB)</span>
    <span class="absolute inset-y-0 left-1/2 border-l border-dashed border-accented/70" />
    <span v-for="row in SCOREBOARD_ROWS - 1" :key="row" class="flex-1 border-b border-dashed border-accented/70" />
    <span class="flex-1" />
  </div>
</template>
