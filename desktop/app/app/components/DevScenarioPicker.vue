<script setup lang="ts">
// Dev-only: `app.vue` renders this behind `import.meta.dev` and only outside
// Tauri, where there is no client to read a real state from.
const { scenarios, current, select } = useDevScenarios()
</script>

<template>
  <div class="fixed bottom-3 left-1/2 z-50 -translate-x-1/2">
    <div class="flex flex-wrap items-center gap-1 rounded-full border border-default bg-elevated/90 px-2 py-1 backdrop-blur">
      <span class="px-1 text-xs uppercase tracking-wide text-dimmed">Scenario</span>
      <UButton
        v-for="scenario in scenarios"
        :key="scenario.id"
        size="xs"
        :variant="current === scenario.id ? 'solid' : 'ghost'"
        :color="current === scenario.id ? 'primary' : 'neutral'"
        @click="select(scenario.id)"
      >
        {{ scenario.label }}
      </UButton>
    </div>
  </div>
</template>
