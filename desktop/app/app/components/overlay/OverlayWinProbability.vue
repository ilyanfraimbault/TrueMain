<script setup lang="ts">
import type { GameState } from '~/types/game'
import { winProbability } from '~/utils/item-value'

/**
 * Each side's chance to win, on screen for the whole game: the product
 * owner's formula (`winProbability`) over the item-gold gap and the map —
 * turrets, inhibitors down, drakes, the Baron's and the Elder's buffs. Our
 * side on the left in blue, theirs on the right in red (blue and red sides
 * when spectating) — the colours say which is which, so no labels.
 */
const props = defineProps<{ game: GameState, syncedAt: number }>()

const { items } = useStaticData()

// The game is read every couple of seconds and only a change is sent: an
// inhibitor standing again or a buff running out is a matter of the clock,
// which runs here between two readings.
const now = ref(Date.now())
let ticking: ReturnType<typeof setInterval> | undefined
onMounted(() => {
  ticking = setInterval(() => (now.value = Date.now()), 1000)
})
onBeforeUnmount(() => clearInterval(ticking))
const clock = computed(() => props.game.gameTime + Math.max(0, now.value - props.syncedAt) / 1000)

const ours = computed(() => Math.round(winProbability(props.game, items.value, clock.value) * 100))
</script>

<template>
  <div class="flex flex-col gap-1.5">
    <p class="flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wider text-dimmed">
      <AppMark class="size-2.5" />
      Win probability
    </p>
    <div class="flex items-end justify-between gap-3">
      <span class="stat-value text-2xl leading-none tabular-nums text-ally">{{ ours }}<span class="text-sm">%</span></span>
      <span class="stat-value text-2xl leading-none tabular-nums text-enemy">{{ 100 - ours }}<span class="text-sm">%</span></span>
    </div>
    <div class="flex h-1 overflow-hidden rounded-full bg-elevated">
      <span class="bg-ally/80 transition-[width] duration-700" :style="{ width: `${ours}%` }" />
      <span class="flex-1 bg-enemy/80" />
    </div>
  </div>
</template>
