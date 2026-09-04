<script setup lang="ts">
import type { ItemContextCard } from '~~/shared/utils/item-context'
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
  /**
   * The situational verdict for this item (#1451). Set by the build-tree and variation
   * call sites, which know which slot they are asking about; absent everywhere else, and
   * the card then renders exactly as it did before.
   */
  context?: ItemContextCard
}>(), {
  item: null,
  width: 36,
  height: 36,
  loading: undefined,
  pickRate: undefined,
  context: undefined,
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
          :context="context"
        />
      </GameTooltipSurface>
    </template>
  </UTooltip>
</template>
