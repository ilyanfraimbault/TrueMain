<script setup lang="ts">
// Loading placeholder for HomeTierlistPanel — same chrome (header + row
// layout) so swapping the two never shifts layout. Extracted (#972) so it can
// render eagerly alongside the panel itself, from the moment the page mounts,
// instead of only appearing behind a since-removed intersection-observer gate
// (which left a blank gap while the panel's own chunk was still loading).
const ROW_COUNT = 8
</script>

<template>
  <section
    class="flex flex-col gap-3"
    aria-hidden="true"
  >
    <header class="flex min-h-6 items-center justify-between">
      <span class="text-xs font-semibold uppercase tracking-wide text-muted">Strongest picks</span>
      <UButton
        to="/champions"
        color="primary"
        variant="link"
        size="xs"
        trailing-icon="i-lucide-arrow-right"
        label="Full tier list"
      />
    </header>
    <div class="surface flex flex-col rounded-2xl py-1.5">
      <div class="px-4 py-2">
        <USkeleton class="h-2.5 w-40" />
      </div>
      <div
        v-for="i in ROW_COUNT"
        :key="i"
        class="flex items-center gap-3 px-4 py-2"
      >
        <USkeleton class="size-8 rounded-md" />
        <USkeleton class="h-4 w-32" />
        <USkeleton class="ml-auto h-4 w-24" />
      </div>
    </div>
  </section>
</template>
