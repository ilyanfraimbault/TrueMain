<script setup lang="ts">
// Shared empty-state for not-yet-built panels. Keeps every stub page visually
// identical so later agents only swap the page body, not the chrome.
defineProps<{
  title: string
  icon: string
  description?: string
}>()
</script>

<template>
  <div class="flex flex-col items-center justify-center text-center py-24 px-4">
    <div class="p-3 rounded-full bg-primary/10 ring ring-inset ring-primary/25 mb-4">
      <UIcon :name="icon" class="size-7 text-primary" />
    </div>
    <h2 class="text-lg font-semibold text-highlighted">
      {{ title }}
    </h2>
    <p class="text-sm text-muted mt-1 max-w-md">
      {{ description ?? 'This panel is coming soon.' }}
    </p>
    <UBadge color="neutral" variant="subtle" class="mt-4" label="Coming soon" />
  </div>
</template>
