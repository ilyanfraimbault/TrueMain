<script setup lang="ts">
/**
 * The head of a page: its icon and title, as the reference client sets them,
 * with the page's own controls on the right.
 */
defineProps<{ title: string, icon: string }>()
</script>

<template>
  <header class="flex items-center gap-3">
    <UIcon :name="icon" class="size-6 text-primary" />
    <h1 class="text-2xl font-semibold tracking-tight text-highlighted">{{ title }}</h1>
    <div class="ml-auto flex items-center gap-2">
      <slot />
    </div>
  </header>
</template>
