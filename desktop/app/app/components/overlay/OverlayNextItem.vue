<script setup lang="ts">
import type { GameState } from '~/types/game'

/**
 * The overlay's next item: one item and whether the gold in hand buys it —
 * nothing a glance over the game cannot take in. No components and no
 * runners-up: which component to buy first is not measured yet, so the
 * overlay does not pretend to know. The game page's panel (`GameNextItem`)
 * keeps the full answer; both read `useNextItemPanel`.
 */
const props = defineProps<{ game: GameState }>()

const { statics, top, remaining, state, name } = useNextItemPanel(toRef(props, 'game'))

/** The gold still to earn before it can be bought; 0 once the gold in hand covers it. */
const missing = computed(() => Math.max(0, remaining.value - props.game.gold))

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
  <OverlayNextItemCard v-if="state === 'ready' && top" :item="statics[top.itemId] ?? null" :name="name(top.itemId)" :missing="missing" />
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
