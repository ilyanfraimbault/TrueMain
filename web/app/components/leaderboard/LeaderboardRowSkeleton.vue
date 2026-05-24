<script setup lang="ts">
// Same column shape as LeaderboardRow.vue so placeholders don't shift the
// layout when the data resolves.
</script>

<template>
  <div
    class="flex items-center gap-3 rounded-md border border-default/40 bg-elevated/20 px-3 py-2"
    aria-hidden="true"
  >
    <USkeleton class="h-4 w-8" />
    <USkeleton class="size-5 rounded-full" />
    <USkeleton class="size-10 rounded" />
    <div class="min-w-0 flex-1 space-y-1">
      <USkeleton class="h-4 w-32" />
      <USkeleton class="h-3 w-16" />
    </div>
    <div class="w-32 shrink-0 space-y-1 text-right">
      <USkeleton class="ml-auto h-4 w-20" />
      <USkeleton class="ml-auto h-3 w-12" />
    </div>
    <div class="hidden shrink-0 items-center gap-1 md:flex">
      <USkeleton class="size-7 rounded" />
      <USkeleton class="size-7 rounded" />
      <USkeleton class="size-7 rounded" />
    </div>
    <div class="ml-auto hidden shrink-0 items-center gap-5 sm:flex">
      <div class="flex w-12 flex-col items-end gap-1">
        <USkeleton class="h-4 w-10" />
        <USkeleton class="h-3 w-8" />
      </div>
      <div class="flex w-12 flex-col items-end gap-1">
        <USkeleton class="h-4 w-10" />
        <USkeleton class="h-3 w-8" />
      </div>
      <div class="flex w-12 flex-col items-end gap-1">
        <USkeleton class="h-4 w-10" />
        <USkeleton class="h-3 w-8" />
      </div>
    </div>
  </div>
</template>
