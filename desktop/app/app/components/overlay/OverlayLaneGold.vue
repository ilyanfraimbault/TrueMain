<script setup lang="ts">
import type { GameState } from '~/types/game'
import { lanePlates } from '~/utils/lane-gold'
import { SCOREBOARD_ROWS } from '~/utils/scoreboard-geometry'

/**
 * The lane-gold plates (#2186): the game's TAB scoreboard's five rows, each
 * holding the item-gold gap of the two players on it, centred on the row.
 * Fills its box, which the shell sizes to the rows (`scoreboard::plates_rect`):
 * the rows are its height split in five. Nothing but the plates is drawn, the
 * window being see-through. A sixth row and beyond (a custom game's extra
 * players) has no row on the scoreboard to sit on.
 */
const props = defineProps<{ game: GameState }>()

const { items } = useStaticData()
const plates = computed(() => {
  const rows = lanePlates(props.game, items.value).slice(0, SCOREBOARD_ROWS)
  return Array.from({ length: SCOREBOARD_ROWS }, (_, index) => rows[index] ?? null)
})
</script>

<template>
  <ol class="flex size-full flex-col">
    <li v-for="(plate, index) in plates" :key="index" class="flex min-h-0 flex-1 items-center justify-center" :data-row="index">
      <OverlayGoldPlate v-if="plate" :plate="plate" />
    </li>
  </ol>
</template>
