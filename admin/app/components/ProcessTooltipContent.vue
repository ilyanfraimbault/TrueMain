<script setup lang="ts">
// The #content slot shared by every process chip's UTooltip: the process
// description above a smaller, muted context line. Reading PROCESS_META and
// resolving the description happen once here instead of once per template
// interpolation at each of the three call sites (chain chip, iteration chip,
// iteration detail row).
import { computed } from 'vue'
import { PROCESS_META } from '~~/shared/types/pipeline-chain'

const props = defineProps<{
  processName: string
  context: string
}>()

const description = computed(() => PROCESS_META[props.processName]?.description)
</script>

<template>
  <p v-if="description" class="text-xs text-highlighted max-w-56 text-pretty">
    {{ description }}
  </p>
  <p class="text-xs text-dimmed" :class="{ 'mt-1': description }">
    {{ context }}
  </p>
</template>
