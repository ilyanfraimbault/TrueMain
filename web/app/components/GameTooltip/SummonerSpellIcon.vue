<script setup lang="ts">
import { computed } from 'vue'
import type { StaticSummonerSpellData } from '~~/shared/types/static-data'

defineOptions({ inheritAttrs: false })

const props = withDefaults(defineProps<{
  spell?: StaticSummonerSpellData | null
  width?: number | string
  height?: number | string
  /** Native lazy-loading hint forwarded to the icon (`'lazy'` below the fold). */
  loading?: 'lazy' | 'eager'
  /** Optional fallback label rendered (and used as tooltip text) when no icon URL is available. */
  fallbackLabel?: string
  /** True while the summoner-spell static map is still loading — see `SkeletonImage`'s `pending`. */
  pending?: boolean
}>(), {
  spell: null,
  width: 36,
  height: 36,
  loading: undefined,
  fallbackLabel: '',
  pending: false,
})

const hasSpell = computed(() => Boolean(props.spell))
// Rendered by `SkeletonImage` itself when the spell has no icon URL yet, so the
// tooltip trigger is always the same element. Branching to a second element
// here would swap that node once the static data lands, and Reka only ever
// reads the trigger node at mount — the stale reference leaves the tooltip
// unable to close on pointer exit, so hovering the icons in turn stacked their
// tooltips on screen instead of replacing them.
const fallbackText = computed(() => props.fallbackLabel || props.spell?.name || '')
</script>

<template>
  <UTooltip
    :disabled="!hasSpell"
    :delay-duration="150"
    :ui="{ content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }"
  >
    <SkeletonImage
      v-bind="$attrs"
      :src="spell?.iconUrl"
      :alt="spell?.name"
      :fallback="fallbackText"
      :pending="pending"
      :width="width"
      :height="height"
      :loading="loading"
    />
    <template
      v-if="spell"
      #content
    >
      <GameTooltipSurface>
        <GameTooltipSummonerSpellBody :spell="spell" />
      </GameTooltipSurface>
    </template>
  </UTooltip>
</template>
