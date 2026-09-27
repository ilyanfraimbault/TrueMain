<script setup lang="ts">
/**
 * A champion select played by hand, through the same draft screen the live
 * one uses — every pick suggestion, build and lane answer is the real API's.
 */
const { draft, label, activeSlot, tick } = useDraftSimulator()

let clock: ReturnType<typeof setInterval> | undefined
onMounted(() => (clock = setInterval(tick, 1000)))
onBeforeUnmount(() => clearInterval(clock))
</script>

<template>
  <div class="flex h-full flex-col">
    <SimulatorBar class="shrink-0 border-b border-default" />
    <DraftScreen :draft="draft" :label="label" :active-slot="activeSlot" class="min-h-0 flex-1" />
  </div>
</template>
