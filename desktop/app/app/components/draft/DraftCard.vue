<script setup lang="ts">
import { laneIconUrl } from '~/types/draft'

/**
 * One pick of a team, as the reference client draws it: the champion's tall
 * loading art, its tier on its lane in the corner, the card's floor washed in
 * that tier's colour. A hairline in the side's colour says whose it is.
 *
 * Rings, in order of weight: `selected` (picked up to swap), a dashed drop
 * target, `active` (the slot the simulator is filling), `viewed` (its build is
 * the one on screen), `opponent` for the lane opponent, `me` for us.
 */
const props = withDefaults(defineProps<{
  championId?: number | null
  team: 'ally' | 'enemy'
  tier?: string | null
  /** The lane glyph an empty slot shows. */
  lane?: string | null
  me?: boolean
  opponent?: boolean
  viewed?: boolean
  /** Hovered, not locked in yet. */
  tentative?: boolean
  active?: boolean
  selected?: boolean
  dropTarget?: boolean
  /** Portraits to suggest in our own empty slot. */
  suggested?: number[]
}>(), { championId: null, tier: null, lane: null, suggested: () => [] })

const { nameOf, portraitOf } = useChampionStatics()

const ring = computed(() => {
  if (props.selected) return 'ring-2 ring-primary ring-offset-2 ring-offset-ink-950'
  if (props.dropTarget) return 'ring-2 ring-dashed ring-accented'
  if (props.active) return 'ring-2 ring-primary/80 shadow-[0_0_24px_-6px_var(--color-rosegold-400)]'
  if (props.viewed) return 'ring-2 ring-white/85 shadow-[0_0_20px_-6px_white]'
  if (props.opponent) return 'ring-2 ring-gold shadow-[0_0_20px_-8px_var(--color-gold)]'
  if (props.me) return 'ring-2 ring-primary shadow-[0_0_20px_-8px_var(--color-rosegold-400)]'
  return 'ring-1 ring-white/5'
})

const tierColor = computed(() => {
  const letter = props.tier?.toLowerCase()
  return letter && 'sabcd'.includes(letter) ? `var(--color-tier-${letter})` : 'transparent'
})
</script>

<template>
  <div
    class="relative size-full overflow-hidden rounded-lg bg-elevated transition-[box-shadow,opacity]"
    :class="ring"
    :style="{ '--tier': tierColor }"
    :title="championId ? nameOf(championId) : undefined"
  >
    <span class="absolute inset-x-0 top-0 z-10 h-0.5" :class="team === 'ally' ? 'bg-ally/70' : 'bg-enemy/70'" />

    <template v-if="championId">
      <ChampionArt
        :champion-id="championId"
        kind="loading"
        fade="none"
        position="50% 14%"
        class="transition"
        :class="tentative && 'opacity-55 grayscale'"
      />
      <div class="card-floor pointer-events-none absolute inset-0" />
      <TierMark v-if="tier && !tentative" :tier="tier" class="absolute bottom-2 left-2" />
      <span v-if="tentative" class="absolute inset-x-0 bottom-2 text-center stat-label text-muted!">Hovering</span>
    </template>

    <div v-else class="flex size-full flex-col items-center justify-center">
      <img v-if="lane" :src="laneIconUrl(lane)" alt="" class="size-7 opacity-25">
      <div v-if="suggested.length" class="absolute inset-x-0 bottom-2 flex flex-col items-center gap-1">
        <span class="stat-label text-[9px]!">Suggested</span>
        <div class="flex -space-x-1">
          <template v-for="id in suggested.slice(0, 3)" :key="id">
            <img v-if="portraitOf(id)" :src="portraitOf(id)!" :alt="nameOf(id)" class="size-5 rounded ring-1 ring-ink-950">
          </template>
        </div>
      </div>
    </div>

    <slot />
  </div>
</template>
