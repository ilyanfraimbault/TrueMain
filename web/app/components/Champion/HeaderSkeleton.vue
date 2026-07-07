<script setup lang="ts">
// Mirrors Header.vue's layout (size-20 icon + name + stats subline) so the
// placeholder occupies the same flex-1 slot beside the filters and the header
// doesn't jump when the champion resolves.
</script>

<template>
  <div class="flex flex-1 flex-wrap items-center gap-4" aria-hidden="true">
    <USkeleton class="size-20 rounded" />
    <div class="flex flex-1 flex-col gap-2">
      <USkeleton class="h-8 w-40" />
      <USkeleton class="h-4 w-56" />
    </div>
  </div>
</template>
