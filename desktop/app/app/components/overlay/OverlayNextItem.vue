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
  <div class="flex items-center gap-2.5">
    <template v-if="state === 'ready' && top">
      <GameTooltipItemIcon :item="statics[top.itemId] ?? null" :width="36" :height="36" class="size-9 shrink-0 rounded-md ring-1 ring-primary/60" />
      <div class="min-w-0 flex-1">
        <p class="flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wider text-dimmed">
          <AppMark class="size-2.5" />
          Next item
        </p>
        <p class="truncate text-[13px] font-semibold leading-tight text-highlighted">{{ name(top.itemId) }}</p>
        <p v-if="missing === 0" class="flex items-center gap-1 text-[11px] font-medium text-stat-gold">
          <UIcon name="i-lucide-circle-check" class="size-3" />
          Can buy now
        </p>
        <p v-else class="text-[11px] tabular-nums text-muted">
          <span class="text-stat-gold">{{ missing }}</span> gold to go
        </p>
      </div>
    </template>

    <template v-else>
      <USkeleton v-if="!message" class="size-9 shrink-0 rounded-md" />
      <AppMark v-else class="size-4 shrink-0" />
      <div class="min-w-0 flex-1">
        <p class="text-[10px] font-semibold uppercase tracking-wider text-dimmed">Next item</p>
        <USkeleton v-if="!message" class="mt-1 h-3 w-28" />
        <p v-else class="text-[11px] text-muted">{{ message }}</p>
      </div>
    </template>
  </div>
</template>
