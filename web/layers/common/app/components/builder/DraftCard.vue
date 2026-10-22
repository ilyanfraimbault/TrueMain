<script setup lang="ts">
import type { ChampionStaticListItem } from '#shared/types/static-data'
import { championLoadingUrlFromIcon, getPositionIconUrl } from '#shared/utils/ddragon'

/**
 * One pick of the matchup board, drawn the way the desktop app's champion
 * select draws it (`DraftCard`): the champion's tall loading art, its floor
 * darkened, a hairline in the side's colour at the top. The card *is* the
 * control: a click opens the champion search for its slot.
 *
 * One ring in TrueMain's colour marks the player's own pick (`you`); a faint one
 * of the same colour marks the role opponent across the board, so the matchup
 * reads as a pair rather than as a second selection. Every other card is a
 * draft slot that only refines the sample.
 *
 * Wordless like the stage it replaces (#1067, #1071): the art names a filled
 * card, the lane glyph an empty one, and the slot's name lives in its
 * `aria-label`.
 */
const props = withDefaults(defineProps<{
  champion: ChampionStaticListItem | null
  side: 'ally' | 'enemy'
  lane: string
  /** The player's own pick, or the role opponent: the matchup itself. */
  mark?: 'you' | 'opponent' | null
  /** The slot's accessible name, e.g. "Ally jungle". */
  label: string
}>(), { mark: null })

defineEmits<{ click: [] }>()

const loadingArt = useCanonicalLoadingArt()
const art = computed(() => loadingArt(championLoadingUrlFromIcon(props.champion?.iconUrl)))

const ring = computed(() => {
  if (props.mark === 'you') return 'ring-2 ring-primary shadow-[0_0_20px_-8px_var(--color-rosegold-400)]'
  if (props.mark === 'opponent') return 'ring-2 ring-primary/35'
  return 'ring-1 ring-white/5 hover:ring-accented'
})

const placeholder = computed(() => {
  if (props.mark === 'you') return 'You'
  if (props.mark === 'opponent') return 'Opponent'
  return null
})
</script>

<template>
  <button
    type="button"
    class="group relative block aspect-[308/560] w-full cursor-pointer overflow-hidden rounded-lg bg-elevated outline-none transition-[box-shadow,filter] hover:brightness-110 focus-visible:ring-2 focus-visible:ring-primary"
    :class="ring"
    :aria-label="champion ? `${label} — ${champion.name}` : `${label} — pick a champion`"
    :title="champion?.name"
    @click="$emit('click')"
  >
    <span
      class="absolute inset-x-0 top-0 z-10 h-0.5"
      :class="side === 'ally' ? 'bg-ally/70' : 'bg-enemy/70'"
    />

    <template v-if="champion">
      <img
        v-if="art"
        :key="art"
        :src="art"
        alt=""
        class="img-skeleton absolute inset-0 size-full object-cover object-[50%_14%]"
        decoding="async"
      >
      <span class="pointer-events-none absolute inset-0 bg-gradient-to-t from-ink-950/85 to-transparent to-45%" />
    </template>

    <span
      v-else
      class="absolute inset-0 flex flex-col items-center justify-center gap-2"
    >
      <img
        :src="getPositionIconUrl(lane)"
        alt=""
        class="size-6 opacity-25 transition-opacity group-hover:opacity-50 sm:size-7"
      >
      <span
        v-if="placeholder"
        class="stat-label"
        :class="mark === 'you' && 'text-primary!'"
      >{{ placeholder }}</span>
      <UIcon
        v-else
        name="i-lucide-plus"
        class="size-4 text-dimmed opacity-0 transition-opacity group-hover:opacity-100"
      />
    </span>
  </button>
</template>
