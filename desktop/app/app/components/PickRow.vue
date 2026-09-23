<script setup lang="ts">
import { laneIconUrl } from '~/types/draft'

/**
 * One pick in a team column: a strip of the champion's splash, with the lane
 * it holds pinned across the card's outer edge. The same slot on both sides of
 * the draft, tinted by side and mirrored for the enemy.
 *
 * Rings, in order of weight: `selected` (picked up to swap), a dashed drop
 * target, `viewed` (its build is the one on screen), `gold` for the opponent
 * we face, `primary` for us, then the side's own tint. An empty slot is an
 * outline that still carries its lane.
 */
const props = withDefaults(defineProps<{
  championId?: number | null
  lane?: string | null
  team: 'ally' | 'enemy'
  gold?: boolean
  primary?: boolean
  viewed?: boolean
  selected?: boolean
  dropTarget?: boolean
  /** Hovered, not locked in yet. */
  tentative?: boolean
}>(), { championId: null, lane: null, gold: false, primary: false, viewed: false, selected: false, dropTarget: false, tentative: false })

const { nameOf } = useChampionStatics()

const ring = computed(() => {
  if (props.selected) return 'ring-2 ring-primary ring-offset-2 ring-offset-ink-950'
  if (props.dropTarget) return 'ring-2 ring-dashed ring-accented'
  if (props.viewed) return 'ring-2 ring-white/90 shadow-[0_0_22px_-4px_white]'
  if (props.gold) return 'ring-2 ring-gold shadow-[0_0_22px_-6px_var(--color-gold)]'
  if (props.primary) return 'ring-2 ring-primary shadow-[0_0_22px_-6px_var(--color-rosegold-400)]'
  return props.team === 'ally' ? 'ring-1 ring-ally/45' : 'ring-1 ring-enemy/45'
})
</script>

<template>
  <div class="relative size-full">
    <div
      v-if="championId"
      class="relative size-full overflow-hidden rounded-xl bg-ink-900 transition-[box-shadow]"
      :class="ring"
      :title="nameOf(championId)"
    >
      <ChampionArt
        :champion-id="championId"
        fade="none"
        position="60% 25%"
        class="transition"
        :class="tentative && 'opacity-50 grayscale'"
      />
      <slot />
    </div>
    <div
      v-else
      class="size-full rounded-xl border border-dashed"
      :class="[team === 'ally' ? 'border-ally/25 bg-ally/5' : 'border-enemy/25 bg-enemy/5', dropTarget && 'ring-2 ring-dashed ring-accented']"
    />

    <!-- The lane, astride the card's outer edge — there before the pick is, so an empty slot still says whose it is. -->
    <img
      v-if="lane"
      :src="laneIconUrl(lane)"
      alt=""
      class="absolute top-1/2 size-6 -translate-y-1/2 rounded-full bg-ink-950 p-1 ring-1 ring-white/15"
      :class="[team === 'ally' ? '-left-3' : '-right-3', !championId && 'opacity-60']"
    >
  </div>
</template>
