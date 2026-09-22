<script setup lang="ts">
/**
 * A tall pick card: the champion's loading-screen art with a strip of meaning
 * at the top and the name at the bottom, the way a draft tool lays out a team.
 *
 * Three rings, in order of weight. `gold` marks the one card that matters most
 * on the screen — the opponent we face. `primary` is interaction: the card the
 * user picked up, or a lane they pinned. A dashed ring is a drop target.
 * An empty slot is a slot, not an error: drawn as an outline, never as an id.
 */
const props = withDefaults(defineProps<{
  championId?: number | null
  name?: string | null
  gold?: boolean
  selected?: boolean
  dropTarget?: boolean
  dimmed?: boolean
}>(), { championId: null, name: null, gold: false, selected: false, dropTarget: false, dimmed: false })

const ring = computed(() => {
  if (props.selected) return 'ring-2 ring-primary shadow-[0_0_0_4px_color-mix(in_oklch,var(--color-rosegold-500)_25%,transparent)]'
  if (props.gold) return 'ring-2 ring-gold shadow-[0_0_24px_-6px_color-mix(in_oklch,var(--color-gold)_55%,transparent)]'
  if (props.dropTarget) return 'ring-2 ring-dashed ring-accented'
  return 'ring-1 ring-default'
})
</script>

<template>
  <div
    class="relative aspect-[3/4] overflow-hidden rounded-xl bg-ink-900 transition-[box-shadow,transform] duration-200"
    :class="[ring, dimmed && 'opacity-50 saturate-50']"
  >
    <ChampionArt v-if="championId" :champion-id="championId" kind="loading" fade="y" position="50% 15%" />

    <div v-else class="absolute inset-0 flex items-center justify-center rounded-xl border border-dashed border-accented text-dimmed">
      <UIcon name="i-lucide-help-circle" class="size-6 opacity-60" />
    </div>

    <!-- The strip of meaning: a lane, a tag, a confidence. -->
    <div class="absolute inset-x-0 top-0 flex items-start justify-between gap-1 p-2">
      <slot name="top" />
    </div>

    <div class="absolute inset-x-0 bottom-0 p-2.5">
      <slot name="bottom">
        <p class="truncate text-sm font-semibold text-highlighted">{{ name ?? '—' }}</p>
      </slot>
    </div>
  </div>
</template>
