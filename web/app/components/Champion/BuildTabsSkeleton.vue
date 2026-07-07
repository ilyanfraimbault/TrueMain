<script setup lang="ts">
// Mirrors BuildTabs.vue: a full-width pill tab bar over the core-view card and
// the stacked build/rune sections below. Heights approximate the real panel so
// the section fills in without a layout jump when the build data resolves.
</script>

<template>
  <div class="flex w-full flex-col gap-4" aria-hidden="true">
    <div class="flex gap-2">
      <USkeleton
        v-for="i in 3"
        :key="`tab-${i}`"
        class="h-9 flex-1 rounded-md"
      />
    </div>
    <USkeleton class="h-64 w-full rounded-lg" />
    <USkeleton class="h-40 w-full rounded-lg" />
  </div>
</template>
