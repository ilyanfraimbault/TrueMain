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
    class="glass rounded-2xl p-3 sm:p-4"
    aria-hidden="true"
  >
    <header class="flex items-center justify-between gap-3 pb-3">
      <span class="text-sm font-semibold text-default">Tier list</span>
      <UButton
        to="/champions"
        color="neutral"
        variant="ghost"
        size="sm"
        trailing-icon="i-lucide-arrow-right"
        label="Full tier list"
      />
    </header>
    <div class="space-y-1">
      <div
        v-for="i in ROW_COUNT"
        :key="i"
        class="-mx-2 flex items-center gap-3 rounded-lg px-2 py-2"
      >
        <USkeleton class="size-9 rounded-lg" />
        <USkeleton class="h-4 w-32" />
        <USkeleton class="ml-auto h-4 w-24" />
      </div>
    </div>
  </section>
</template>
