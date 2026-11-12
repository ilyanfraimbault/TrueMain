<script setup lang="ts">
// The #content slot shared by every process chip's UTooltip: the process
// description above a smaller, muted context line. Resolving the description from
// the pipeline catalog happens once here instead of once per template interpolation
// at each of the three call sites (chain chip, iteration chip, iteration detail row).
import { computed } from 'vue'
import { usePipelineCatalog } from '~/composables/usePipelineCatalog'
import { chainDescription } from '~/utils/process-chain'

const props = defineProps<{
  processName: string
  context: string
}>()

const catalog = usePipelineCatalog()
const description = computed(() => chainDescription(catalog.value, props.processName))
</script>

<template>
  <p v-if="description" class="text-xs text-highlighted max-w-56 text-pretty">
    {{ description }}
  </p>
  <p class="text-xs text-dimmed" :class="{ 'mt-1': description }">
    {{ context }}
  </p>
</template>
