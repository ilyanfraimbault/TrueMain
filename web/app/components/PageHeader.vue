<script setup lang="ts">
// Shared page-level heading: rose eyebrow + display title (+ optional
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
      <p class="text-sm font-medium text-primary">
        {{ eyebrow }}
      </p>
      <h1 class="text-2xl font-semibold tracking-tight text-highlighted sm:text-3xl">
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
