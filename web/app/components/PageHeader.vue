<script setup lang="ts">
// Shared page-level heading: mono eyebrow + display title (+ optional
// description), with a default slot below for filter strips. Keeps the
// champions / truemains headers in lockstep instead of duplicating the
// eyebrow markup per page.
defineProps<{
  eyebrow: string
  title: string
  description?: string
}>()
</script>

<template>
  <header class="space-y-3">
    <div class="space-y-1">
      <p class="eyebrow">
        {{ eyebrow }}
      </p>
      <h1 class="text-2xl font-medium text-highlighted text-balance sm:text-4xl">
        {{ title }}
      </h1>
      <p
        v-if="description"
        class="text-sm text-muted"
      >
        {{ description }}
      </p>
    </div>
    <slot />
  </header>
</template>
