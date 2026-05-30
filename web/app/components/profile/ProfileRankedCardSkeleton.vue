<script setup lang="ts">
defineProps<{ noop?: never }>()
</script>

<template>
  <section class="flex flex-col gap-3 rounded-lg bg-elevated/40 px-4 py-3">
    <USkeleton class="h-3 w-24" />
    <div class="flex items-center gap-3">
      <USkeleton class="size-12 rounded-full" />
      <div class="flex flex-col gap-1.5">
        <USkeleton class="h-4 w-32" />
        <USkeleton class="h-3 w-24" />
      </div>
    </div>
    <div class="flex gap-2">
      <USkeleton class="h-6 w-24 rounded-md" />
      <USkeleton class="h-6 w-20 rounded-md" />
    </div>
    <USkeleton class="h-[150px] w-full rounded-md" />
  </section>
</template>
