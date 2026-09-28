<script setup lang="ts">
import { laneIconUrl } from '~/types/draft'

/**
 * One pick of a team, as the reference client draws it: the champion's tall
 * loading art, its tier on its lane in the corner, the card's floor washed in
 * that tier's colour. A hairline in the side's colour says whose it is.
 *
 * One ring, in TrueMain's colour, marks the card whose build and lane are on
 * screen (`selected`); a faint one of the same colour marks its lane opponent
 * across the board, so the duel reads as a pair rather than as a second
 * selection. A dragged enemy shows its drop targets. The slot
 * being filled is not a ring — two would read as two selections — but its
 * side's hairline, thickened and pulsing.
 */
const props = withDefaults(defineProps<{
  championId?: number | null
  team: 'ally' | 'enemy'
  tier?: string | null
  /** The lane glyph an empty slot shows. */
  lane?: string | null
  /** Hovered, not locked in yet. */
  tentative?: boolean
  /** The slot being filled. */
  active?: boolean
  /** Its build and lane are the ones on screen. */
  selected?: boolean
  /** The selected champion's lane opponent. */
  opponent?: boolean
  dropTarget?: boolean
  /** Portraits to suggest in our own empty slot. */
  suggested?: number[]
}>(), { championId: null, tier: null, lane: null, suggested: () => [] })

const { nameOf, portraitOf } = useChampionStatics()

const ring = computed(() => {
  if (props.dropTarget) return 'ring-2 ring-accented'
  if (props.selected) return 'ring-2 ring-primary shadow-[0_0_20px_-8px_var(--color-rosegold-400)]'
  if (props.opponent) return 'ring-2 ring-primary/35'
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
    <span
      class="absolute inset-x-0 top-0 z-10"
      :class="[team === 'ally' ? 'bg-ally' : 'bg-enemy', active ? 'h-1 animate-pulse' : 'h-0.5 opacity-70']"
    />

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
      <img v-if="lane" :src="laneIconUrl(lane)" alt="" class="size-7" :class="active ? 'animate-pulse opacity-60' : 'opacity-25'">
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
