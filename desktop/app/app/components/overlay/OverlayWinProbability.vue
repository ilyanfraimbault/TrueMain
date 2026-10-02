<script setup lang="ts">
import type { GameState } from '~/types/game'
import { itemGold, leftTeam, winProbability } from '~/utils/item-value'

/**
 * Each side's chance to win, on screen for the whole game: an estimate from
 * the item-gold gap alone (`winProbability`), the product owner's call. Our
 * side on the left in blue, theirs on the right in red (blue and red sides
 * when spectating) — the colours say which is which, so no labels.
 */
const props = defineProps<{ game: GameState }>()

const { items } = useStaticData()

const left = computed(() => leftTeam(props.game))
const gold = (ours: boolean) => props.game.players
  .filter(player => (player.team === left.value) === ours)
  .reduce((sum, player) => sum + itemGold(player, items.value), 0)

const ours = computed(() => Math.round(winProbability(gold(true), gold(false)) * 100))
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
