<script setup lang="ts">
import type { GameState } from '~/types/game'
import { basketName } from '~/utils/next-item'

/**
 * The overlay's build path (#2183): the next item and whether the gold in
 * hand buys it, its components in the order the mains buy them — owned,
 * bought now, later — and under it the build as a row (#2101), to see where
 * the game stands and when the boots come. No runners-up: a glance over the
 * game takes in one decision. Until a starter is bought, the starter stands
 * in for it: it is what the shop is open for at the start. The game page's
 * panel (`GameNextItem`) keeps the full answer; both read `useNextItemPanel`.
 */
const props = defineProps<{ game: GameState }>()

const { statics, order, top, remaining, path, state, name, starter, starterGold } = useNextItemPanel(toRef(props, 'game'))

/** The gold still to earn before it can be bought; 0 once the gold in hand covers it. */
const missing = computed(() => Math.max(0, remaining.value - props.game.gold))

/** The basket's face: its first item that is not a potion. */
const starterItem = computed(() => {
  const ids = starter.value?.itemIds ?? []
  const face = ids.find(id => !statics.value[id]?.tags?.includes('Consumable')) ?? ids[0]
  return face === undefined ? null : statics.value[face] ?? null
})

const message = computed(() => {
  switch (state.value) {
    case 'unmeasured': return 'No measured build on this lane yet'
    case 'complete': return 'Build complete'
    case 'offline': return 'TrueMain is unreachable'
    default: return null
  }
})
</script>

<template>
  <OverlayNextItemCard v-if="starter" label="Starter" :item="starterItem" :name="basketName(starter.itemIds, name)" :missing="Math.max(0, starterGold - game.gold)" />
  <div v-else-if="state === 'ready' && top" class="space-y-1.5">
    <OverlayNextItemCard :item="statics[top.itemId] ?? null" :name="name(top.itemId)" :missing="missing" />
    <GameBuildPath v-if="path" :path="path" :statics="statics" :size="20" />
    <!-- The route needs no rule above it: the path's prices already end it. -->
    <GameBuildOrder :steps="order" :statics="statics" :size="18" />
  </div>
  <div v-else class="flex items-center gap-2.5">
    <USkeleton v-if="!message" class="size-9 shrink-0 rounded-md" />
    <AppMark v-else class="size-4 shrink-0" />
    <div class="min-w-0 flex-1">
      <p class="text-[10px] font-semibold uppercase tracking-wider text-dimmed">Next item</p>
      <USkeleton v-if="!message" class="mt-1 h-3 w-28" />
      <p v-else class="text-[11px] text-muted">{{ message }}</p>
    </div>
  </div>
</template>
