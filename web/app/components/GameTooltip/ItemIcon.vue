<script setup lang="ts">
import { computed } from 'vue'
import type { StaticItemData } from '~~/shared/types/static-data'

defineOptions({ inheritAttrs: false })

const props = withDefaults(defineProps<{
  item?: StaticItemData | null
  width?: number | string
  height?: number | string
  /** Native lazy-loading hint forwarded to the icon (`'lazy'` below the fold). */
  loading?: 'lazy' | 'eager'
  /** Optional pickrate (0..1) — only set by BuildTree call sites; renders next to the item name. */
  pickRate?: number
}>(), {
  item: null,
  width: 36,
  height: 36,
  loading: undefined,
  pickRate: undefined,
})

const hasItem = computed(() => Boolean(props.item))
</script>

<template>
  <UTooltip
    :disabled="!hasItem"
    :delay-duration="150"
    :ui="{ content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }"
  >
    <SkeletonImage
      v-bind="$attrs"
      :src="item?.iconUrl"
      :alt="item?.name"
      :width="width"
      :height="height"
      :loading="loading"
    />
    <template
      v-if="item"
      #content
    >
      <GameTooltipSurface>
        <GameTooltipItemBody
          :item="item"
          :pick-rate="pickRate"
        />
      </GameTooltipSurface>
    </template>
  </UTooltip>
</template>
