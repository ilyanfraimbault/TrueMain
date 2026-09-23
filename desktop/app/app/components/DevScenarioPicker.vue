<script setup lang="ts">
// Dev-only: `app.vue` renders this behind `import.meta.dev` and only outside
// Tauri, where there is no client to read a real state from. Kept to a corner
// select so it does not cover the screen being worked on.
const { scenarios, current, select } = useDevScenarios()

const items = computed(() => scenarios.value.map(scenario => ({ label: scenario.label, value: scenario.id })))
const model = computed({
  get: () => current.value,
  set: (id: string) => select(id),
})
</script>

<template>
  <div class="fixed bottom-3 right-3 z-50">
    <USelect v-model="model" :items="items" size="xs" icon="i-lucide-flask-conical" class="w-64" />
  </div>
</template>
