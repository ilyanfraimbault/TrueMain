<script setup lang="ts">
import { computed } from 'vue'
import type { StaticPerkData } from '~~/shared/types/static-data'

defineOptions({ inheritAttrs: false })

const props = withDefaults(defineProps<{
  perk?: StaticPerkData | null
  width?: number | string
  height?: number | string
}>(), {
  perk: null,
  width: 36,
  height: 36,
})

const hasPerk = computed(() => Boolean(props.perk))
</script>

<template>
  <UTooltip
    :disabled="!hasPerk"
    :delay-duration="150"
    :ui="{ content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }"
  >
    <SkeletonImage
      v-bind="$attrs"
      :src="perk?.iconUrl"
      :alt="perk?.name"
      :width="width"
      :height="height"
    />
    <template
      v-if="perk"
      #content
    >
      <GameTooltipSurface>
        <GameTooltipPerkBody :perk="perk" />
      </GameTooltipSurface>
    </template>
  </UTooltip>
</template>
