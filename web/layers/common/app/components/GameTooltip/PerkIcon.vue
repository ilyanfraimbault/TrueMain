<script setup lang="ts">
import { computed } from 'vue'
import type { RuneSheet, StaticPerkData } from '#shared/types/static-data'
import { findRuneSheet } from '#shared/utils/rune-sheet'

defineOptions({ inheritAttrs: false })

const props = withDefaults(defineProps<{
  perk?: StaticPerkData | null
  width?: number | string
  height?: number | string
  /** Native lazy-loading hint forwarded to the icon (`'lazy'` below the fold). */
  loading?: 'lazy' | 'eager'
  /**
   * The rune tree's sprite sheets (#999): when one holds this perk, the icon is
   * cut from it instead of fetched alone. Only a square, numeric size is cut.
   */
  sheets?: RuneSheet[] | null
}>(), {
  perk: null,
  width: 36,
  height: 36,
  loading: undefined,
  sheets: null,
})

const hasPerk = computed(() => Boolean(props.perk))

const sheet = computed(() => {
  const { sheets, perk, width, height } = props
  if (!perk || typeof width !== 'number' || width !== height) return null
  return findRuneSheet(sheets, perk.id)
})
</script>

<template>
  <GameTooltipLazyTooltip
    :disabled="!hasPerk"
    :delay-duration="150"
    :ui="{ content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }"
  >
    <GameTooltipPerkSheetIcon
      v-if="sheet && perk && typeof width === 'number'"
      v-bind="$attrs"
      :sheet="sheet"
      :perk-id="perk.id"
      :src="perk.iconUrl"
      :alt="perk.name"
      :size="width"
    />
    <SkeletonImage
      v-else
      v-bind="$attrs"
      :src="perk?.iconUrl"
      transparent
      :alt="perk?.name"
      :width="width"
      :height="height"
      :loading="loading"
    />
    <template
      v-if="perk"
      #content
    >
      <GameTooltipSurface>
        <GameTooltipPerkBody :perk="perk" />
      </GameTooltipSurface>
    </template>
  </GameTooltipLazyTooltip>
</template>
