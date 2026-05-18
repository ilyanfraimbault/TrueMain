<script setup lang="ts">
import { computed } from 'vue'
import type { StaticPerkStyleData } from '~~/shared/types/static-data'

defineOptions({ inheritAttrs: false })

const props = withDefaults(defineProps<{
  style?: StaticPerkStyleData | null
  width?: number | string
  height?: number | string
}>(), {
  style: null,
  width: 36,
  height: 36,
})

const hasStyle = computed(() => Boolean(props.style))
const tooltipText = computed(() => props.style?.name ?? '')
</script>

<template>
  <UTooltip
    :disabled="!hasStyle"
    :delay-duration="150"
    :text="tooltipText"
  >
    <SkeletonImage
      v-bind="$attrs"
      :src="style?.iconUrl"
      :alt="style?.name"
      :width="width"
      :height="height"
    />
  </UTooltip>
</template>
