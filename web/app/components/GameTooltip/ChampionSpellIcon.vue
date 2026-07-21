<script setup lang="ts">
import { computed } from 'vue'
import type { StaticChampionSpellData } from '~~/shared/types/static-data'

defineOptions({ inheritAttrs: false })

const props = withDefaults(defineProps<{
  spell?: StaticChampionSpellData | null
  width?: number | string
  height?: number | string
  /** Native lazy-loading hint forwarded to the icon (`'lazy'` below the fold). */
  loading?: 'lazy' | 'eager'
  /** Fallback label shown when no icon URL is available (e.g. the slot key 'Q'). */
  fallbackLabel?: string
}>(), {
  spell: null,
  width: 36,
  height: 36,
  loading: undefined,
  fallbackLabel: '',
})

const hasSpell = computed(() => Boolean(props.spell))
const hasIcon = computed(() => Boolean(props.spell?.iconUrl))
const fallbackText = computed(() => props.fallbackLabel || props.spell?.key || '')
const fallbackStyle = computed(() => ({
  width: typeof props.width === 'number' ? `${props.width}px` : props.width,
  height: typeof props.height === 'number' ? `${props.height}px` : props.height,
}))
</script>

<template>
  <UTooltip
    :disabled="!hasSpell"
    :delay-duration="150"
    :ui="{ content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }"
  >
    <SkeletonImage
      v-if="hasIcon"
      v-bind="$attrs"
      :src="spell?.iconUrl"
      :alt="spell?.name"
      :width="width"
      :height="height"
      :loading="loading"
    />
    <span
      v-else
      v-bind="$attrs"
      class="inline-flex items-center justify-center rounded border border-default text-xs"
      :style="fallbackStyle"
    >
      {{ fallbackText }}
    </span>
    <template
      v-if="spell"
      #content
    >
      <GameTooltipSurface>
        <GameTooltipChampionSpellBody :spell="spell" />
      </GameTooltipSurface>
    </template>
  </UTooltip>
</template>
