<script setup lang="ts">
import type { GameState } from '~/types/game'
import type { HudSettings } from '~/types/overlay'
import { DEFAULT_HUD } from '~/types/overlay'
import { PANEL_WIDTHS } from '~/utils/overlay-layout'
import { platesBox } from '~/utils/scoreboard-geometry'

/**
 * A 1920 × 1080 screen, small, with the game's HUD outlined and the lane-gold
 * plates (#2186) on the scoreboard's rows — for the game simulator, which
 * shows them as each reading of the tape lands. The plates keep their real
 * size: on a screen this small they look larger than in game.
 */
const props = withDefaults(defineProps<{ game: GameState, hud?: HudSettings, nudge?: number }>(), { hud: () => DEFAULT_HUD, nudge: 0 })

const SCREEN = { width: 1920, height: 1080 }
const box = computed(() => {
  const plates = platesBox(SCREEN, props.hud, props.nudge, PANEL_WIDTHS['lane-gold'])
  const pct = (value: number, of: number) => `${(value / of) * 100}%`
  return { top: pct(plates.y, SCREEN.height), height: pct(plates.height, SCREEN.height) }
})
</script>

<template>
  <div class="relative aspect-video w-full overflow-hidden rounded-lg bg-[radial-gradient(ellipse_at_40%_35%,var(--ui-bg-accented),var(--ui-bg-muted)_70%)] ring-1 ring-default">
    <OverlayScreenHud :screen="SCREEN" :scoreboard="hud" />
    <div class="absolute left-1/2 -translate-x-1/2" :style="box" data-plates>
      <OverlayLaneGold :game="game" />
    </div>
  </div>
</template>
