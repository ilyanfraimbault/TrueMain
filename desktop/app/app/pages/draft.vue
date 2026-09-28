<script setup lang="ts">
/**
 * Champion select — live or rehearsed, one page. While the client is in
 * champion select this is the live draft, and the gameflow phase opens it on
 * its own (`app.vue`). The rest of the time it is the same draft screen played
 * by hand, the simulator's bar over it; a real champion select takes the page
 * over the moment one starts.
 */
const { state, screen } = useLcuState()
const live = computed(() => screen.value === 'draft' && state.value.draft !== null)

const { draft, label, activeSlot, tick } = useDraftSimulator()

// The rehearsal's clock runs only while it is on screen.
let clock: ReturnType<typeof setInterval> | undefined
onMounted(() => {
  clock = setInterval(() => {
    if (!live.value) tick()
  }, 1000)
})
onBeforeUnmount(() => clearInterval(clock))
</script>

<template>
  <DraftScreen v-if="live && state.draft" :draft="state.draft" />

  <div v-else class="flex h-full flex-col">
    <SimulatorBar class="shrink-0 border-b border-default" />
    <DraftScreen :draft="draft" :label="label" :active-slot="activeSlot" class="min-h-0 flex-1" />
  </div>
</template>
