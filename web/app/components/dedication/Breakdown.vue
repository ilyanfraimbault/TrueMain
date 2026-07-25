<script setup lang="ts">
import type { DedicationComponent } from '~/utils/dedication'

// The four dedication components as bars, shared by the profile card and the
// leaderboard row's tooltip so the two surfaces can't drift apart visually.
defineProps<{
  components: DedicationComponent[]
}>()
</script>

<template>
  <ul class="flex flex-col gap-2">
    <li
      v-for="component in components"
      :key="component.key"
      class="flex flex-col gap-1"
    >
      <div class="flex items-baseline justify-between gap-2 text-[11px]">
        <span class="font-medium text-default">{{ component.label }}</span>
        <span class="truncate text-muted">{{ component.detail }}</span>
      </div>
      <div class="h-1.5 w-full overflow-hidden rounded-full bg-elevated/60">
        <div
          class="h-full rounded-full bg-primary/70"
          :style="{ width: `${Math.round(component.value * 100)}%` }"
        />
      </div>
    </li>
  </ul>
</template>
