<script setup lang="ts">
import { computed } from 'vue'
import type { StaticPerkStyleData } from '~~/shared/types/static-data'

defineOptions({ inheritAttrs: false })

const props = withDefaults(defineProps<{
  style?: StaticPerkStyleData | null
  width?: number | string
  height?: number | string
  /** Native lazy-loading hint forwarded to the icon (`'lazy'` below the fold). */
  loading?: 'lazy' | 'eager'
}>(), {
  style: null,
  width: 36,
  height: 36,
  loading: undefined,
})

const hasStyle = computed(() => Boolean(props.style))
</script>

<template>
  <GameTooltipLazyTooltip
    :disabled="!hasStyle"
    :delay-duration="150"
    :ui="{ content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }"
  >
    <SkeletonImage
      v-bind="$attrs"
      :src="style?.iconUrl"
      :alt="style?.name"
      :width="width"
      :height="height"
      :loading="loading"
    />
    <template
      v-if="style"
      #content
    >
      <GameTooltipSurface>
        <GameTooltipPerkStyleBody :perk-style="style" />
      </GameTooltipSurface>
    </template>
  </GameTooltipLazyTooltip>
</template>
