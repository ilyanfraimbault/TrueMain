<script setup lang="ts">
import type { TruemainMark } from '~/utils/player-intel'
import { markFigures } from '~/utils/player-intel'

/**
 * The TrueMain mark beside a player TrueMain tracks as a true main of the
 * champion they are on (#1910): the logo alone, no text — a tag's name is
 * what players misread — and what it means in its tooltip, with the figures
 * the lookup returned.
 */
const props = defineProps<{ mark: TruemainMark }>()

const { nameOf } = useChampionStatics()

const title = computed(() => `True main of ${nameOf(props.mark.championId)}`)
const figures = computed(() => markFigures(props.mark))
const label = computed(() => [title.value, ...figures.value].join(' · '))
</script>

<template>
  <UTooltip
    :delay-duration="0"
    :ui="{ content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }"
  >
    <span role="img" tabindex="0" :aria-label="label" class="inline-flex shrink-0 self-center rounded-sm outline-none focus-visible:ring-1 focus-visible:ring-primary">
      <AppMark class="size-3" />
    </span>
    <template #content>
      <GameTooltipSurface>
        <p class="text-sm font-semibold text-highlighted">{{ title }}</p>
        <p v-for="figure in figures" :key="figure" class="mt-0.5 text-xs tabular-nums text-muted">{{ figure }}</p>
      </GameTooltipSurface>
    </template>
  </UTooltip>
</template>
