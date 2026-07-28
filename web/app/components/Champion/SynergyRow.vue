<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { POSITION_BY_VALUE } from '~/utils/positions'

const props = withDefaults(defineProps<{
  champion: ChampionStaticListItem | null
  /** Fallback label when the static list has no entry for the id. */
  championId: number
  position: string
  games: number
  winRate: number
  /** Observed minus expected win rate — the value the list is ranked by. */
  synergy: number
  /** Renders as a button that reports clicks, for the partner list. */
  selectable?: boolean
  selected?: boolean
}>(), { selectable: false, selected: false })

const emit = defineEmits<{ select: [] }>()

const positionOption = computed(() => POSITION_BY_VALUE.get(props.position) ?? null)

// The synergy is the headline number, so it carries the colour. Near-zero stays
// neutral on purpose: a pairing that lands where its parts predicted is neither
// good nor bad news, and painting ±0.3 points green would invent a signal.
const NEUTRAL_BAND = 0.01
const synergyClass = computed(() => {
  if (Math.abs(props.synergy) < NEUTRAL_BAND) return 'text-muted'
  return props.synergy > 0 ? 'text-emerald-400' : 'text-red-400'
})
const synergyLabel = computed(() => {
  const points = props.synergy * 100
  const rounded = Math.round(points * 10) / 10
  return `${rounded > 0 ? '+' : ''}${rounded.toFixed(1)}`
})
</script>

<template>
  <component
    :is="selectable ? 'button' : 'div'"
    :type="selectable ? 'button' : undefined"
    :aria-pressed="selectable ? selected : undefined"
    class="flex w-full items-center gap-3 rounded-md px-2 py-1.5 text-left transition-colors"
    :class="[
      selectable ? 'glass-hover cursor-pointer' : 'hover:bg-elevated/40',
      selected ? 'bg-elevated/60 ring-1 ring-inset ring-primary/40' : '',
    ]"
    @click="selectable && emit('select')"
  >
    <SkeletonImage
      v-if="champion?.iconUrl"
      :src="champion.iconUrl"
      :alt="champion.name"
      width="32"
      height="32"
      class="size-8 shrink-0 rounded"
    />
    <div v-else class="size-8 shrink-0 rounded bg-elevated" aria-hidden="true" />

    <SkeletonImage
      v-if="positionOption"
      :src="positionOption.iconUrl"
      :alt="positionOption.label"
      :width="16"
      :height="16"
      class="size-4 shrink-0 opacity-70"
    />

    <span class="min-w-0 flex-1 truncate text-sm text-default">
      {{ champion?.name ?? `Champion ${championId}` }}
    </span>

    <!-- Sample size and raw win rate stay visible next to the synergy: the
         ranking value is a difference, and a difference means nothing without
         the two numbers and the count it came from. -->
    <span class="shrink-0 text-xs tabular-nums text-muted">
      {{ games.toLocaleString('en-US') }} games
    </span>
    <span class="w-12 shrink-0 text-right text-xs tabular-nums text-muted">
      {{ formatPercentage(winRate, 0) }}
    </span>
    <span
      class="w-14 shrink-0 text-right text-sm font-semibold tabular-nums"
      :class="synergyClass"
    >
      {{ synergyLabel }}
    </span>
  </component>
</template>
